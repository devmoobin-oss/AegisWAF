using System.Collections.Concurrent;
using System.Net;
using Npgsql;
using AegisWAF.Gateway.Sites;

namespace AegisWAF.Gateway;

/// <summary>Resolves the originating client only when the direct peer is explicitly trusted.</summary>
internal static class ClientAddress
{
    public static bool IsTrustedPeer(HttpContext ctx, IConfiguration cfg) =>
        ctx.Connection.RemoteIpAddress is not null && IsTrusted(ctx.Connection.RemoteIpAddress, cfg);

    public static string Resolve(HttpContext ctx, IConfiguration cfg)
    {
        var peer = ctx.Connection.RemoteIpAddress;
        if (peer is null) return "local";
        if (!IsTrusted(peer, cfg)) return peer.ToString();

        var forwarded = ctx.Request.Headers["X-Forwarded-For"].ToString();
        var candidates = forwarded.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = candidates.Length - 1; i >= 0; i--)
            if (IPAddress.TryParse(candidates[i], out var address) && !IsTrusted(address, cfg)) return address.ToString();
        return peer.ToString();
    }

    public static bool IsTrusted(IPAddress address, IConfiguration cfg)
    {
        var entries = cfg.GetSection("Security:TrustedProxies").Get<string[]>() ?? [];
        return entries.Any(x => Cidr.Contains(x, address));
    }
}

internal static class Cidr
{
    public static bool TryNormalize(string value, out string normalized)
    {
        normalized = "";
        if (!TryParse(value, out var network, out var bits)) return false;
        var bytes = network.GetAddressBytes();
        MaskNetwork(bytes, bits);
        normalized = bits == bytes.Length * 8 ? new IPAddress(bytes).ToString() : $"{new IPAddress(bytes)}/{bits}";
        return true;
    }

    public static int PrefixLength(string value) => TryParse(value, out _, out var bits) ? bits : -1;

    public static bool Contains(string value, IPAddress address)
    {
        if (!TryParse(value, out var network, out var bits) || network.AddressFamily != address.AddressFamily) return false;
        var a = address.GetAddressBytes();
        var n = network.GetAddressBytes();
        var full = bits / 8; var rem = bits % 8;
        for (var i = 0; i < full; i++) if (a[i] != n[i]) return false;
        return rem == 0 || (a[full] & (byte)(0xff << (8 - rem))) == (n[full] & (byte)(0xff << (8 - rem)));
    }

    private static bool TryParse(string value, out IPAddress network, out int bits)
    {
        network = IPAddress.None;
        bits = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var trimmed = value.Trim();
        var slash = trimmed.IndexOf('/');
        if (slash < 0)
        {
            if (!IPAddress.TryParse(trimmed, out var parsed) || parsed is null) return false;
            network = parsed;
            bits = network.GetAddressBytes().Length * 8;
            return true;
        }
        if (slash == 0 || trimmed.IndexOf('/', slash + 1) >= 0 || !IPAddress.TryParse(trimmed[..slash], out var parsedNetwork) || parsedNetwork is null) return false;
        network = parsedNetwork;
        if (!int.TryParse(trimmed[(slash + 1)..], out bits)) return false;
        return bits >= 0 && bits <= network.GetAddressBytes().Length * 8;
    }

    private static void MaskNetwork(byte[] bytes, int bits)
    {
        var full = bits / 8;
        var rem = bits % 8;
        if (rem != 0)
        {
            bytes[full] = (byte)(bytes[full] & (byte)(0xff << (8 - rem)));
            full++;
        }
        for (var i = full; i < bytes.Length; i++) bytes[i] = 0;
    }
}

/// <summary>Rolling-window limiter with per-IP protection and an optional global ceiling.</summary>
sealed class SlidingRateLimiter : IDisposable
{
    private sealed class Bucket
    {
        public readonly object Gate = new();
        public readonly Queue<long> Hits = new();
    }

    private readonly ConcurrentDictionary<string, Bucket> _buckets = new(StringComparer.Ordinal);
    private readonly int _limit;
    private readonly int _globalLimit;
    private readonly TimeSpan _window;
    private readonly int _maxEntries;
    private readonly ConcurrentDictionary<string, Bucket> _globalBuckets = new(StringComparer.Ordinal);
    private readonly Timer _cleanupTimer;
    private readonly PostgresRateLimiter? _shared;

    public SlidingRateLimiter(IConfiguration cfg)
    {
        _limit = Math.Clamp(cfg.GetValue("Waf:RateLimit:Requests", 300), 1, 1_000_000);
        _globalLimit = Math.Clamp(cfg.GetValue("Waf:RateLimit:GlobalRequests", 0), 0, 10_000_000);
        _window = TimeSpan.FromSeconds(Math.Clamp(cfg.GetValue("Waf:RateLimit:WindowSeconds", 60), 1, 3600));
        _maxEntries = Math.Clamp(cfg.GetValue("Waf:RateLimit:MaxEntries", 10_000), 100, 1_000_000);
        if (string.Equals(cfg["Waf:RateLimit:Store"], "postgres", StringComparison.OrdinalIgnoreCase))
            _shared = new PostgresRateLimiter(cfg.GetConnectionString("Postgres") ?? string.Empty, _limit, _globalLimit, _window, _maxEntries);
        _cleanupTimer = new(_ => PurgeExpired(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - (long)_window.TotalMilliseconds),
            null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public bool Allow(string ip) => Allow("global", ip, "/");

    public bool Allow(string siteId, string ip, string route, int? limitOverride = null, int? globalLimitOverride = null, TimeSpan? windowOverride = null)
    {
        var limit = Math.Clamp(limitOverride ?? _limit, 1, 1_000_000);
        var globalLimit = Math.Clamp(globalLimitOverride ?? _globalLimit, 0, 10_000_000);
        var window = windowOverride ?? _window;
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var cutoff = now - (long)window.TotalMilliseconds;
        var key = RateLimitKey.Build(siteId, route, ip);
        if (!_buckets.ContainsKey(key) && _buckets.Count >= _maxEntries)
        {
            PurgeExpired(cutoff);
            // A full table must fail closed for new identities. This prevents an attacker
            // from creating unbounded buckets or evicting active clients at random.
            if (_buckets.Count >= _maxEntries) return false;
        }
        var bucket = _buckets.GetOrAdd(key, _ => new Bucket());
        lock (bucket.Gate)
        {
            while (bucket.Hits.Count > 0 && bucket.Hits.Peek() <= cutoff) bucket.Hits.Dequeue();
            if (bucket.Hits.Count >= limit) return false;
            if (globalLimit > 0)
            {
                var global = _globalBuckets.GetOrAdd(RateLimitKey.Site(siteId), _ => new Bucket());
                lock (global.Gate)
                {
                    while (global.Hits.Count > 0 && global.Hits.Peek() <= cutoff) global.Hits.Dequeue();
                    if (global.Hits.Count >= globalLimit) return false;
                    global.Hits.Enqueue(now);
                }
            }
            bucket.Hits.Enqueue(now);
        }
        return true;
    }

    public Task<bool> AllowAsync(string ip, CancellationToken ct = default) => AllowAsync("global", ip, "/", ct);

    public Task<bool> AllowAsync(string ip, string route, CancellationToken ct = default) => AllowAsync("global", ip, route, ct);

    public Task<bool> AllowAsync(string siteId, string ip, string route, CancellationToken ct = default) =>
        _shared is null ? Task.FromResult(Allow(siteId, ip, route)) : _shared.AllowAsync(siteId, ip, route, _limit, _globalLimit, null, ct);

    public Task<bool> AllowAsync(string siteId, string ip, string route, SiteRateLimitPolicy? policy, CancellationToken ct = default) =>
        _shared is null
            ? Task.FromResult(Allow(siteId, ip, route, policy?.Requests, policy?.GlobalRequests,
                policy is null ? null : TimeSpan.FromSeconds(Math.Clamp(policy.WindowSeconds, 1, 3600))))
            : _shared.AllowAsync(siteId, ip, route, policy?.Requests ?? _limit, policy?.GlobalRequests ?? _globalLimit,
                policy is null ? null : TimeSpan.FromSeconds(Math.Clamp(policy.WindowSeconds, 1, 3600)), ct);

    private void PurgeExpired(long cutoff)
    {
        foreach (var global in _globalBuckets)
        {
            lock (global.Value.Gate)
            {
                while (global.Value.Hits.Count > 0 && global.Value.Hits.Peek() <= cutoff) global.Value.Hits.Dequeue();
                if (global.Value.Hits.Count == 0) _globalBuckets.TryRemove(global.Key, out _);
            }
        }
        foreach (var entry in _buckets)
        {
            lock (entry.Value.Gate)
            {
                while (entry.Value.Hits.Count > 0 && entry.Value.Hits.Peek() <= cutoff) entry.Value.Hits.Dequeue();
                if (entry.Value.Hits.Count == 0) _buckets.TryRemove(entry.Key, out _);
            }
        }
    }

    public void Dispose() => _cleanupTimer.Dispose();
}

/// <summary>Optional shared limiter using one PostgreSQL transaction for global and per-IP buckets.</summary>
sealed class PostgresRateLimiter(string connectionString, int perIpLimit, int globalLimit, TimeSpan window, int maxEntries)
{
    private int _failures;
    private long _openUntilMs;

    public Task<bool> AllowAsync(string ip, CancellationToken ct) => AllowAsync("global", ip, "/", ct);

    public async Task<bool> AllowAsync(string ip, string route, CancellationToken ct)
        => await AllowAsync("global", ip, route, perIpLimit, globalLimit, null, ct);

    public async Task<bool> AllowAsync(string siteId, string ip, string route, CancellationToken ct)
        => await AllowAsync(siteId, ip, route, perIpLimit, globalLimit, null, ct);

    public async Task<bool> AllowAsync(string siteId, string ip, string route, int siteLimit, int siteGlobalLimit, TimeSpan? windowOverride, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return false;
        if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() < Interlocked.Read(ref _openUntilMs)) return false;
        try
        {
            await using var db = new NpgsqlConnection(connectionString);
            await db.OpenAsync(ct);
            await using var tx = await db.BeginTransactionAsync(ct);
            var boundedSite = RateLimitKey.Site(siteId);
            var activeWindow = windowOverride ?? window;
            var siteKey = RateLimitKey.Site(siteId);
            var keys = new List<string>(3);
            if (siteGlobalLimit > 0) keys.Add(boundedSite + ":global");
            keys.Add(siteKey + ":ip:" + RateLimitKey.Bounded(ip, 128));
            keys.Add(siteKey + ":route:" + RateLimitKey.Bounded(route, 128) + ":ip:" + RateLimitKey.Bounded(ip, 128));
            if (!await EnsureCapacityAsync(db, tx, keys, activeWindow, ct)) return false;
            var keyIndex = 0;
            if (siteGlobalLimit > 0 && !await ConsumeAsync(db, tx, keys[keyIndex++], siteGlobalLimit, activeWindow, ct)) return false;
            if (!await ConsumeAsync(db, tx, keys[keyIndex++], siteLimit, activeWindow, ct)) return false;
            if (!await ConsumeAsync(db, tx, keys[keyIndex], siteLimit, activeWindow, ct)) return false;
            await tx.CommitAsync(ct);
            Interlocked.Exchange(ref _failures, 0);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is NpgsqlException or TimeoutException or InvalidOperationException)
        {
            // Shared enforcement is security-critical: an unavailable store must not fail open.
            if (Interlocked.Increment(ref _failures) >= 3)
                Interlocked.Exchange(ref _openUntilMs, DateTimeOffset.UtcNow.AddSeconds(5).ToUnixTimeMilliseconds());
            return false;
        }
    }

    private async Task<bool> EnsureCapacityAsync(NpgsqlConnection db, NpgsqlTransaction tx, IReadOnlyList<string> keys, TimeSpan activeWindow, CancellationToken ct)
    {
        const string lockAndCleanup = """
            SELECT pg_advisory_xact_lock(hashtextextended('aegiswaf:rate-limit-capacity', 0));
            DELETE FROM waf_rate_limit_buckets
              WHERE updated_at < now() - @retention;
            """;
        const string countsSql = """
            SELECT
                COUNT(*)::bigint AS total_rows,
                COUNT(*) FILTER (WHERE bucket_key = ANY(@keys))::bigint AS existing_rows
              FROM waf_rate_limit_buckets;
            """;
        await using (var cleanup = new NpgsqlCommand(lockAndCleanup, db, tx))
        {
            cleanup.Parameters.AddWithValue("retention", activeWindow > window ? activeWindow : window);
            await cleanup.ExecuteNonQueryAsync(ct);
        }
        await using var cmd = new NpgsqlCommand(countsSql, db, tx);
        cmd.Parameters.AddWithValue("keys", keys.ToArray());
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return false;
        var totalRows = reader.GetInt64(0);
        var existingRows = reader.GetInt64(1);
        var candidateCount = keys.Distinct(StringComparer.Ordinal).Count();
        return RateLimitCapacityPolicy.Allows(totalRows, existingRows, candidateCount, maxEntries);
    }

    private async Task<bool> ConsumeAsync(NpgsqlConnection db, NpgsqlTransaction tx, string key, int limit, TimeSpan activeWindow, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO waf_rate_limit_buckets(bucket_key, window_started, request_count, updated_at)
            VALUES (@key, now(), 1, now())
            ON CONFLICT (bucket_key) DO UPDATE SET
                window_started = CASE WHEN waf_rate_limit_buckets.window_started <= now() - @window THEN now() ELSE waf_rate_limit_buckets.window_started END,
                request_count = CASE WHEN waf_rate_limit_buckets.window_started <= now() - @window THEN 1 ELSE waf_rate_limit_buckets.request_count + 1 END,
                updated_at = now()
            RETURNING request_count
            """;
        await using var cmd = new NpgsqlCommand(sql, db, tx);
        cmd.Parameters.AddWithValue("key", key);
        cmd.Parameters.AddWithValue("window", activeWindow);
        var count = (int)(await cmd.ExecuteScalarAsync(ct) ?? limit + 1);
        return count <= limit;
    }
}

internal static class RateLimitCapacityPolicy
{
    public static bool Allows(long totalRows, long existingCandidateRows, int candidateKeyCount, int maxEntries)
    {
        if (totalRows < 0 || existingCandidateRows < 0 || candidateKeyCount < 0 || maxEntries < 1) return false;
        if (existingCandidateRows > candidateKeyCount || existingCandidateRows > totalRows) return false;
        var missing = candidateKeyCount - existingCandidateRows;
        return totalRows <= maxEntries && totalRows - existingCandidateRows + missing <= maxEntries;
    }
}

internal static class RateLimitKey
{
    public static string Build(string siteId, string route, string ip) =>
        Site(siteId) + ":route:" + Bounded(route, 128) + ":ip:" + Bounded(ip, 128);

    public static string Site(string siteId) => "site:" + Bounded(siteId, 64);

    public static string Bounded(string value, int max) =>
        string.IsNullOrWhiteSpace(value) ? "unknown" : value.Length > max ? value[..max] : value;
}
