namespace AegisWAF.Gateway.Auth;

public sealed record ThrottleDecision(bool Allowed, TimeSpan RetryAfter, string Reason);

/// <summary>Bounded in-memory throttle for failed authentication attempts, separate from request traffic limits.</summary>
public sealed class AuthThrottle
{
    private sealed record Entry(DateTimeOffset WindowStart, int Failures);
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly int _maxFailures;
    private readonly TimeSpan _window;
    private readonly int _maxEntries;

    public AuthThrottle(int maxFailures = 5, TimeSpan? window = null, int maxEntries = 10_000)
    {
        _maxFailures = Math.Clamp(maxFailures, 1, 100);
        _window = window.GetValueOrDefault(TimeSpan.FromMinutes(15));
        if (_window <= TimeSpan.Zero) _window = TimeSpan.FromMinutes(15);
        _maxEntries = Math.Clamp(maxEntries, 1, 100_000);
    }

    public int EntryCount { get { lock (_gate) return _entries.Count; } }

    public ThrottleDecision Allow(string clientIp, string username, DateTimeOffset now)
    {
        lock (_gate)
        {
            Prune(now);
            TimeSpan? retryAfter = null;
            foreach (var key in Keys(clientIp, username))
            {
                if (!_entries.TryGetValue(key, out var entry) || now - entry.WindowStart >= _window || entry.Failures < _maxFailures) continue;
                var retry = entry.WindowStart.Add(_window) - now;
                if (retryAfter is null || retry > retryAfter.Value) retryAfter = retry;
            }
            return retryAfter is null ? new(true, TimeSpan.Zero, "allowed") : new(false, retryAfter.Value, "throttled");
        }
    }

    public void RecordFailure(string clientIp, string username, DateTimeOffset now)
    {
        lock (_gate)
        {
            Prune(now);
            foreach (var key in Keys(clientIp, username))
            {
                var next = _entries.TryGetValue(key, out var old) && now - old.WindowStart < _window
                    ? new Entry(old.WindowStart, old.Failures + 1)
                    : new Entry(now, 1);
                _entries[key] = next;
            }
            Prune(now);
        }
    }

    public void RecordSuccess(string clientIp, string username)
    {
        lock (_gate)
        {
            // A successful login is positive evidence for this client and clears
            // all local buckets so a prior transient burst cannot strand it.
            _entries.Remove(IpKey(clientIp));
            _entries.Remove(UserKey(username));
            _entries.Remove(PairKey(clientIp, username));
        }
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var key in _entries.Where(x => now - x.Value.WindowStart >= _window).Select(x => x.Key).ToArray()) _entries.Remove(key);
        foreach (var prefix in Prefixes)
        {
            var keys = _entries.Where(x => x.Key.StartsWith(prefix, StringComparison.Ordinal))
                .OrderBy(x => x.Value.WindowStart).Select(x => x.Key).ToArray();
            foreach (var key in keys.Take(Math.Max(0, keys.Length - _maxEntries))) _entries.Remove(key);
        }
    }

    private static readonly string[] Prefixes = ["ip:", "user:", "pair:"];

    private static IEnumerable<string> Keys(string ip, string username)
    {
        yield return IpKey(ip);
        yield return UserKey(username);
        yield return PairKey(ip, username);
    }

    private static string IpKey(string value) => "ip:" + Normalize(value);
    private static string UserKey(string value) => "user:" + Normalize(value);
    private static string PairKey(string ip, string username) => "pair:" + Normalize(ip) + "\u001f" + Normalize(username);

    private static string Normalize(string value)
    {
        var normalized = (value ?? "unknown").Trim().ToLowerInvariant();
        return normalized.Length > 128 ? normalized[..128] : normalized;
    }
}
