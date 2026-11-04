namespace AegisWAF.Client;

/// <summary>Builds a bounded, redacted, local-only recovery timeline for operator handoff.</summary>
public static class SiteHealthSummaryTimelineExporter
{
    public const int MaxExportLength = 1024;
    public const int MaxEventsPerKind = 3;

    public static string Export(
        IEnumerable<RefreshCancellationEvent> cancellationEvents,
        IEnumerable<SiteHealthSummaryRecoveryEvent> recoveryEvents,
        RefreshCancellationReason reason = RefreshCancellationReason.None,
        int maxLength = MaxExportLength)
    {
        ArgumentNullException.ThrowIfNull(cancellationEvents);
        ArgumentNullException.ThrowIfNull(recoveryEvents);
        if (maxLength <= 0) return string.Empty;

        var lines = new List<string> { "AegisWAF Multi-site recovery timeline" };
        lines.AddRange(cancellationEvents
            .Where(item => reason is RefreshCancellationReason.None || item.Reason == reason)
            .OrderBy(item => item.Sequence)
            .TakeLast(MaxEventsPerKind)
            .Select(item => $"cancel #{item.Sequence} reason={ToReasonLabel(item.Reason)}"));
        lines.AddRange(recoveryEvents
            .TakeLast(MaxEventsPerKind)
            .Select(ToRecoveryLabel));

        if (lines.Count == 1) lines.Add("no events");
        var output = string.Join(Environment.NewLine, lines);
        return output.Length <= maxLength ? output : output[..maxLength];
    }

    private static string ToReasonLabel(RefreshCancellationReason reason) => reason switch
    {
        RefreshCancellationReason.Superseded => "superseded",
        RefreshCancellationReason.Unloaded => "unloaded",
        RefreshCancellationReason.Retry => "retry",
        _ => "redacted"
    };

    private static string ToRecoveryLabel(SiteHealthSummaryRecoveryEvent item) => item.Kind switch
    {
        "failure" => $"health failure consecutive={Math.Clamp(item.ConsecutiveFailures, 0, int.MaxValue)}",
        "recovered" => $"health recovered after={Math.Clamp(item.ConsecutiveFailures, 0, int.MaxValue)}",
        _ => "health event redacted"
    };
}
