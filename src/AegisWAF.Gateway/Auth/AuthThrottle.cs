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
            if (!_entries.TryGetValue(Key(clientIp, username), out var entry) || now - entry.WindowStart >= _window)
                return new(true, TimeSpan.Zero, "allowed");
            if (entry.Failures < _maxFailures) return new(true, TimeSpan.Zero, "allowed");
            return new(false, entry.WindowStart.Add(_window) - now, "throttled");
        }
    }

    public void RecordFailure(string clientIp, string username, DateTimeOffset now)
    {
        lock (_gate)
        {
            Prune(now);
            var key = Key(clientIp, username);
            var next = _entries.TryGetValue(key, out var old) && now - old.WindowStart < _window
                ? new Entry(old.WindowStart, old.Failures + 1)
                : new Entry(now, 1);
            _entries[key] = next;
            Prune(now);
        }
    }

    public void RecordSuccess(string clientIp, string username)
    {
        lock (_gate) _entries.Remove(Key(clientIp, username));
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var key in _entries.Where(x => now - x.Value.WindowStart >= _window).Select(x => x.Key).ToArray()) _entries.Remove(key);
        if (_entries.Count <= _maxEntries) return;
        foreach (var key in _entries.OrderBy(x => x.Value.WindowStart).Take(_entries.Count - _maxEntries).Select(x => x.Key).ToArray()) _entries.Remove(key);
    }

    private static string Key(string ip, string username) => (ip ?? "unknown").Trim().ToLowerInvariant() + "\u001f" + (username ?? "").Trim().ToLowerInvariant();
}
