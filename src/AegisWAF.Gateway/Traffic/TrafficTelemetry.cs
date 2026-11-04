using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace AegisWAF.Gateway.Traffic;

internal sealed class TrafficTelemetry
{
    internal const string DecisionItem = "AegisWaf.Telemetry.Decision";
    internal const string RuleItem = "AegisWaf.Telemetry.Rule";
    private const int DefaultCapacity = 500;
    private const int MaxCapacity = 5000;
    private readonly object _gate = new();
    private readonly int _capacity;
    private readonly List<TrafficRecord> _records = [];
    private long _activeConnections;
    private long _droppedEvents;
    private long _allowed;
    private long _blocked;
    private long _rateLimited;
    private long _upstreamErrors;

    public TrafficTelemetry(IConfiguration configuration)
    {
        _capacity = Math.Clamp(configuration.GetValue("Waf:Telemetry:LiveCapacity", DefaultCapacity), 1, MaxCapacity);
    }

    public static string SanitizePath(PathString path, QueryString query) => TrafficTelemetryPath.Sanitize(path, query);

    internal static string? ResolveHostname(string? matchedHostname, HostString requestHost)
    {
        if (!string.IsNullOrWhiteSpace(matchedHostname)) return matchedHostname;
        return string.IsNullOrWhiteSpace(requestHost.Host) ? null : requestHost.Host;
    }

    public static void Mark(HttpContext context, TrafficDecision decision, string? rule)
    {
        context.Items[DecisionItem] = decision;
        context.Items[RuleItem] = rule;
    }

    public IDisposable BeginRequest()
    {
        Interlocked.Increment(ref _activeConnections);
        return new RequestScope(this);
    }

    public void Record(TrafficRecord record)
    {
        lock (_gate)
        {
            _records.Insert(0, record);
            if (_records.Count > _capacity)
            {
                _records.RemoveAt(_records.Count - 1);
                _droppedEvents++;
            }

            switch (record.Decision)
            {
                case TrafficDecision.Allowed: _allowed++; break;
                case TrafficDecision.Blocked: _blocked++; break;
                case TrafficDecision.RateLimited: _rateLimited++; break;
                case TrafficDecision.UpstreamError: _upstreamErrors++; break;
            }
        }
    }

    public TrafficTelemetrySummary GetSummary()
    {
        var active = Math.Max(0, Interlocked.Read(ref _activeConnections));
        var since = DateTimeOffset.UtcNow.AddMinutes(-1);
        long recent;
        lock (_gate) recent = _records.LongCount(x => x.Timestamp >= since);
        return new TrafficTelemetrySummary(active, recent, Interlocked.Read(ref _allowed), Interlocked.Read(ref _blocked),
            Interlocked.Read(ref _rateLimited), Interlocked.Read(ref _upstreamErrors), BufferedCount, Interlocked.Read(ref _droppedEvents));
    }

    public IReadOnlyList<TrafficRecord> Query(TrafficQuery query)
    {
        var limit = Math.Clamp(query.Limit, 1, MaxApiLimit);
        var search = query.Search?.Trim();
        lock (_gate)
        {
            return _records.Where(x =>
                    (!query.Decision.HasValue || x.Decision == query.Decision.Value) &&
                    (string.IsNullOrWhiteSpace(query.SiteId) || string.Equals(x.SiteId, query.SiteId.Trim(), StringComparison.OrdinalIgnoreCase)) &&
                    (string.IsNullOrWhiteSpace(query.ClientIp) || string.Equals(x.ClientIp, query.ClientIp.Trim(), StringComparison.OrdinalIgnoreCase)) &&
                    (string.IsNullOrWhiteSpace(search) || ContainsSearch(x, search)))
                .Take(limit)
                .ToArray();
        }
    }

    private const int MaxApiLimit = 500;
    private int BufferedCount { get { lock (_gate) return _records.Count; } }

    private static bool ContainsSearch(TrafficRecord x, string search) =>
        x.Path.Contains(search, StringComparison.OrdinalIgnoreCase) ||
        x.Method.Contains(search, StringComparison.OrdinalIgnoreCase) ||
        x.ClientIp.Contains(search, StringComparison.OrdinalIgnoreCase) ||
        (x.SiteId?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
        (x.Hostname?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
        (x.Rule?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false);

    private void EndRequest() => Interlocked.Decrement(ref _activeConnections);

    private sealed class RequestScope(TrafficTelemetry owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) owner.EndRequest();
        }
    }
}
