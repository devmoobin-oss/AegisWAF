namespace AegisWAF.Client;

public sealed record SiteHealthSummaryRecoveryEvent(DateTimeOffset OccurredAt, string Kind, int ConsecutiveFailures)
{
    public string Display => Kind switch
    {
        "recovered" => $"recovered after {ConsecutiveFailures} failures",
        _ => $"failure · consecutive {ConsecutiveFailures}"
    };
}

/// <summary>Bounded client-side state for rendering the last safe health summary during transient failures.</summary>
public sealed class SiteHealthSummaryRecoveryState
{
    public const int StaleAfterConsecutiveFailures = 2;
    public const int MaxEventHistory = 8;
    public const int MaxDiagnosticsLength = 256;
    public static readonly TimeSpan MaxSummaryAge = TimeSpan.FromMinutes(5);
    private readonly Queue<SiteHealthSummaryRecoveryEvent> _events = new();
    private int _consecutiveFailures;
    private string _correlationId = "00000000";
    private bool _diagnosticsAvailable;

    public SiteHealthSummaryDto? LastSuccessfulSummary { get; private set; }
    public int ConsecutiveFailures => _consecutiveFailures;
    public bool IsStale => LastSuccessfulSummary is not null && _consecutiveFailures >= StaleAfterConsecutiveFailures;
    public IReadOnlyList<SiteHealthSummaryRecoveryEvent> Events => _events.ToArray();
    public bool DiagnosticsAvailable => _diagnosticsAvailable;

    public string BeginRefresh()
    {
        _diagnosticsAvailable = false;
        _correlationId = Guid.NewGuid().ToString("N")[..8];
        return _correlationId;
    }

    public string BuildDiagnostics(DateTimeOffset now, RefreshCancellationReason cancellationReason = RefreshCancellationReason.None)
    {
        var ageSeconds = LastSuccessfulSummaryAge(now) is { } age
            ? Math.Clamp((long)age.TotalSeconds, 0, 999_999)
            : -1;
        var stale = IsStale || IsFreshnessStale(now) ? 1 : 0;
        var reason = cancellationReason switch
        {
            RefreshCancellationReason.Superseded => "superseded",
            RefreshCancellationReason.Unloaded => "unloaded",
            RefreshCancellationReason.Retry => "retry",
            _ => "none"
        };
        _diagnosticsAvailable = true;
        var diagnostics = $"summary-refresh:{_correlationId};failures:{_consecutiveFailures};stale:{stale};age-seconds:{ageSeconds};cancel:{reason}";
        return diagnostics.Length <= MaxDiagnosticsLength ? diagnostics : diagnostics[..MaxDiagnosticsLength];
    }

    public TimeSpan? LastSuccessfulSummaryAge(DateTimeOffset now)
    {
        var generatedAt = LastSuccessfulSummary?.GeneratedAt;
        return generatedAt is null || generatedAt > now ? null : now - generatedAt.Value;
    }

    public bool IsFreshnessStale(DateTimeOffset now) =>
        LastSuccessfulSummaryAge(now) is { } age && age >= MaxSummaryAge;

    public void RecordFailure()
    {
        if (_consecutiveFailures < int.MaxValue) _consecutiveFailures++;
        AddEvent(new SiteHealthSummaryRecoveryEvent(DateTimeOffset.UtcNow, "failure", _consecutiveFailures));
    }

    public void RecordSuccess(SiteHealthSummaryDto summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        if (_consecutiveFailures > 0)
            AddEvent(new SiteHealthSummaryRecoveryEvent(DateTimeOffset.UtcNow, "recovered", _consecutiveFailures));
        _consecutiveFailures = 0;
        LastSuccessfulSummary = summary;
    }

    private void AddEvent(SiteHealthSummaryRecoveryEvent item)
    {
        _events.Enqueue(item);
        while (_events.Count > MaxEventHistory) _events.Dequeue();
    }
}
