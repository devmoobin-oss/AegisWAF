namespace AegisWAF.Gateway.Sites;

public sealed record SiteHealthSummaryFailureSnapshot(
    long TotalFailures,
    long ConsecutiveFailures,
    DateTimeOffset? LastFailureAt,
    DateTimeOffset? LastSuccessAt);

/// <summary>Process-local numeric summary telemetry. It deliberately stores no Site, database, or exception data.</summary>
public sealed class SiteHealthSummaryFailureTelemetry
{
    private readonly object _gate = new();
    private long _totalFailures;
    private long _consecutiveFailures;
    private DateTimeOffset? _lastFailureAt;
    private DateTimeOffset? _lastSuccessAt;

    public SiteHealthSummaryFailureTelemetry(long initialTotalFailures = 0)
    {
        _totalFailures = Math.Clamp(initialTotalFailures, 0, long.MaxValue);
    }

    public SiteHealthSummaryFailureSnapshot RecordFailure()
    {
        lock (_gate)
        {
            if (_totalFailures < long.MaxValue) _totalFailures++;
            if (_consecutiveFailures < long.MaxValue) _consecutiveFailures++;
            _lastFailureAt = DateTimeOffset.UtcNow;
            return SnapshotUnsafe();
        }
    }

    public SiteHealthSummaryFailureSnapshot RecordSuccess()
    {
        lock (_gate)
        {
            _consecutiveFailures = 0;
            _lastSuccessAt = DateTimeOffset.UtcNow;
            return SnapshotUnsafe();
        }
    }

    public SiteHealthSummaryFailureSnapshot Snapshot()
    {
        lock (_gate) return SnapshotUnsafe();
    }

    private SiteHealthSummaryFailureSnapshot SnapshotUnsafe() =>
        new(_totalFailures, _consecutiveFailures, _lastFailureAt, _lastSuccessAt);
}
