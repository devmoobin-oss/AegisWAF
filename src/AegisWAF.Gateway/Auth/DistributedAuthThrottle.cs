using Npgsql;

namespace AegisWAF.Gateway.Auth;

/// <summary>Shared PostgreSQL login-abuse state. Production callers fail closed when the store is unavailable.</summary>
internal sealed class DistributedAuthThrottle(IConfiguration configuration)
{
    private readonly bool _enabled = string.Equals(configuration["Auth:Throttle:Store"], "postgres", StringComparison.OrdinalIgnoreCase);
    private readonly string _connectionString = configuration.GetConnectionString("Postgres") ?? string.Empty;
    private readonly int _maxFailures = Math.Clamp(configuration.GetValue("Auth:Throttle:MaxFailures", 10), 1, 1000);
    private readonly int _maxEntries = Math.Clamp(configuration.GetValue("Auth:Throttle:MaxEntries", 10_000), 100, 1_000_000);
    private readonly TimeSpan _window = TimeSpan.FromMinutes(Math.Clamp(configuration.GetValue("Auth:Throttle:WindowMinutes", 15), 1, 1440));
    private readonly bool _failClosed = configuration.GetValue("Waf:Deployment:Enforce", false);

    public async Task<bool> AllowAsync(string ip, string username, CancellationToken ct)
    {
        if (!_enabled) return true;
        try
        {
            await using var db = new NpgsqlConnection(_connectionString);
            await db.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand("SELECT bucket_key,failure_count,window_started FROM waf_auth_throttle_buckets WHERE bucket_key = ANY(@keys)", db);
            cmd.Parameters.AddWithValue("keys", Keys(ip, username));
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var started = reader.GetFieldValue<DateTimeOffset>(2);
                if (started > DateTimeOffset.UtcNow - _window && reader.GetInt32(1) >= _maxFailures) return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is NpgsqlException or TimeoutException or InvalidOperationException)
        { return !_failClosed ? true : false; }
    }

    public async Task<bool> RecordFailureAsync(string ip, string username, CancellationToken ct)
    {
        if (!_enabled) return true;
        try
        {
            await using var db = new NpgsqlConnection(_connectionString);
            await db.OpenAsync(ct);
            await using var tx = await db.BeginTransactionAsync(ct);
            await using (var cleanup = new NpgsqlCommand("DELETE FROM waf_auth_throttle_buckets WHERE updated_at < now()-@retention", db, tx))
            {
                cleanup.Parameters.AddWithValue("retention", _window + _window);
                await cleanup.ExecuteNonQueryAsync(ct);
            }
            await using (var count = new NpgsqlCommand("SELECT count(*) FROM waf_auth_throttle_buckets WHERE bucket_key <> ALL(@keys)", db, tx))
            {
                count.Parameters.AddWithValue("keys", Keys(ip, username));
                var entries = (long)(await count.ExecuteScalarAsync(ct) ?? 0L);
                if (entries >= _maxEntries) return !_failClosed;
            }
            foreach (var key in Keys(ip, username))
            {
                await using var cmd = new NpgsqlCommand("""
                    INSERT INTO waf_auth_throttle_buckets(bucket_key,window_started,failure_count,updated_at)
                    VALUES (@key,now(),1,now())
                    ON CONFLICT(bucket_key) DO UPDATE SET
                        window_started=CASE WHEN waf_auth_throttle_buckets.window_started <= now()-@window THEN now() ELSE waf_auth_throttle_buckets.window_started END,
                        failure_count=CASE WHEN waf_auth_throttle_buckets.window_started <= now()-@window THEN 1 ELSE waf_auth_throttle_buckets.failure_count+1 END,
                        updated_at=now()
                    """, db, tx);
                cmd.Parameters.AddWithValue("key", key); cmd.Parameters.AddWithValue("window", _window);
                await cmd.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
            return true;
        }
        catch (Exception ex) when (ex is NpgsqlException or TimeoutException or InvalidOperationException)
        { return !_failClosed; }
    }

    public async Task<bool> RecordSuccessAsync(string ip, string username, CancellationToken ct)
    {
        if (!_enabled) return true;
        try
        {
            await using var db = new NpgsqlConnection(_connectionString);
            await db.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand("DELETE FROM waf_auth_throttle_buckets WHERE bucket_key = ANY(@keys)", db);
            cmd.Parameters.AddWithValue("keys", Keys(ip, username));
            await cmd.ExecuteNonQueryAsync(ct);
            return true;
        }
        catch (Exception ex) when (ex is NpgsqlException or TimeoutException or InvalidOperationException)
        { return !_failClosed; }
    }

    private static string[] Keys(string ip, string username) =>
        [$"ip:{Normalize(ip)}", $"user:{Normalize(username)}"];

    private static string Normalize(string value) => (value ?? "unknown").Trim().ToLowerInvariant()[..Math.Min(128, (value ?? "unknown").Trim().Length)];
}
