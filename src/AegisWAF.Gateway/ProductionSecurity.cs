using System.Collections.Concurrent;
using System.Net;
using Npgsql;

namespace AegisWAF.Gateway;

/// <summary>Resolves the originating client only when the direct peer is explicitly trusted.</summary>
internal static class ClientAddress
{
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
    public static bool Contains(string value, IPAddress address)
    {
        var slash = value.IndexOf('/');
        if (slash < 0) return IPAddress.TryParse(value, out var exact) && exact.Equals(address);
        if (!IPAddress.TryParse(value[..slash], out var network) || !int.TryParse(value[(slash + 1)..], out var bits)) return false;
        if (network.AddressFamily != address.AddressFamily) return false;
        var a = address.GetAddressBytes();
        var n = network.GetAddressBytes();
        var maxBits = a.Length * 8;
        if (bits is < 0 || bits > maxBits) return false;
        var full = bits / 8; var rem = bits % 8;
        for (var i = 0; i < full; i++) if (a[i] != n[i]) return false;
        return rem == 0 || (a[full] & (byte)(0xff << (8 - rem))) == (n[full] & (byte)(0xff << (8 - rem)));
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
    private readonly Bucket _global = new();
    private readonly Timer _cleanupTimer;
    private readonly PostgresRateLimiter? _shared;

    public SlidingRateLimiter(IConfiguration cfg)
    {
        _limit = Math.Clamp(cfg.GetValue("Waf:RateLimit:Requests", 300), 1, 1_000_000);
        _globalLimit = Math.Clamp(cfg.GetValue("Waf:RateLimit:GlobalRequests", 0), 0, 10_000_000);
        _window = TimeSpan.FromSeconds(Math.Clamp(cfg.GetValue("Waf:RateLimit:WindowSeconds", 60), 1, 3600));
        _maxEntries = Math.Clamp(cfg.GetValue("Waf:RateLimit:MaxEntries", 10_000), 100, 1_000_000);
        if (string.Equals(cfg["Waf:RateLimit:Store"], "postgres", StringComparison.OrdinalIgnoreCase))
            _shared = new PostgresRateLimiter(cfg.GetConnectionString("Postgres") ?? string.Empty, _limit, _globalLimit, _window);
        _cleanupTimer = new(_ => PurgeExpired(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - (long)_window.TotalMilliseconds),
            null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public bool Allow(string ip)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var cutoff = now - (long)_window.TotalMilliseconds;
        if (!_buckets.ContainsKey(ip) && _buckets.Count >= _maxEntries)
        {
            PurgeExpired(cutoff);
            // A full table must fail closed for new identities. This prevents an attacker
            // from creating unbounded buckets or evicting active clients at random.
            if (_buckets.Count >= _maxEntries) return false;
        }
        var bucket = _buckets.GetOrAdd(ip, _ => new Bucket());
        lock (bucket.Gate)
        {
            while (bucket.Hits.Count > 0 && bucket.Hits.Peek() <= cutoff) bucket.Hits.Dequeue();
            if (bucket.Hits.Count >= _limit) return false;
            if (_globalLimit > 0)
            {
                lock (_global.Gate)
                {
                    while (_global.Hits.Count > 0 && _global.Hits.Peek() <= cutoff) _global.Hits.Dequeue();
                    if (_global.Hits.Count >= _globalLimit) return false;
                    _global.Hits.Enqueue(now);
                }
            }
            bucket.Hits.Enqueue(now);
        }
        return true;
    }

    public Task<bool> AllowAsync(string ip, CancellationToken ct = default) => AllowAsync(ip, "/", ct);

    public Task<bool> AllowAsync(string ip, string route, CancellationToken ct = default) =>
        _shared is null ? Task.FromResult(Allow(ip)) : _shared.AllowAsync(ip, route, ct);

    private void PurgeExpired(long cutoff)
    {
        lock (_global.Gate)
            while (_global.Hits.Count > 0 && _global.Hits.Peek() <= cutoff) _global.Hits.Dequeue();
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
sealed class PostgresRateLimiter(string connectionString, int perIpLimit, int globalLimit, TimeSpan window)
{
    private int _failures;
    private long _openUntilMs;

    public Task<bool> AllowAsync(string ip, CancellationToken ct) => AllowAsync(ip, "/", ct);

    public async Task<bool> AllowAsync(string ip, string route, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return false;
        if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() < Interlocked.Read(ref _openUntilMs)) return false;
        try
        {
            await using var db = new NpgsqlConnection(connectionString);
            await db.OpenAsync(ct);
            await using var tx = await db.BeginTransactionAsync(ct);
            if (globalLimit > 0 && !await ConsumeAsync(db, tx, "global", globalLimit, ct)) return false;
            if (!await ConsumeAsync(db, tx, "ip:" + ip, perIpLimit, ct)) return false;
            var boundedRoute = route.Length > 128 ? route[..128] : route;
            if (!await ConsumeAsync(db, tx, "route:" + boundedRoute + ":ip:" + ip, perIpLimit, ct)) return false;
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

    private async Task<bool> ConsumeAsync(NpgsqlConnection db, NpgsqlTransaction tx, string key, int limit, CancellationToken ct)
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
        cmd.Parameters.AddWithValue("window", window);
        var count = (int)(await cmd.ExecuteScalarAsync(ct) ?? limit + 1);
        return count <= limit;
    }
}
