using AegisWAF.Client;

namespace AegisWAF.Tests;

public sealed class SiteHealthSummaryTimelineExporterTests
{
    [Fact]
    public void Export_orders_cancellations_by_sequence_and_applies_reason_filter()
    {
        var cancellationEvents = new[]
        {
            new RefreshCancellationEvent(2, DateTimeOffset.Parse("2026-10-26T02:00:01Z"), RefreshCancellationReason.Retry),
            new RefreshCancellationEvent(1, DateTimeOffset.Parse("2026-10-26T02:00:02Z"), RefreshCancellationReason.Superseded)
        };

        var all = SiteHealthSummaryTimelineExporter.Export(cancellationEvents, Array.Empty<SiteHealthSummaryRecoveryEvent>());
        var retryOnly = SiteHealthSummaryTimelineExporter.Export(
            cancellationEvents,
            Array.Empty<SiteHealthSummaryRecoveryEvent>(),
            RefreshCancellationReason.Retry);

        Assert.True(all.IndexOf("cancel #1 reason=superseded", StringComparison.Ordinal) <
                    all.IndexOf("cancel #2 reason=retry", StringComparison.Ordinal));
        Assert.Contains("cancel #2 reason=retry", retryOnly, StringComparison.Ordinal);
        Assert.DoesNotContain("superseded", retryOnly, StringComparison.Ordinal);
    }

    [Fact]
    public void Export_redacts_unknown_reason_and_recovery_kind_without_raw_values()
    {
        var cancellationEvents = new[]
        {
            new RefreshCancellationEvent(1, DateTimeOffset.UtcNow, (RefreshCancellationReason)999)
        };
        var recoveryEvents = new[]
        {
            new SiteHealthSummaryRecoveryEvent(DateTimeOffset.UtcNow, "exception:host=secret;database=prod", 7)
        };

        var export = SiteHealthSummaryTimelineExporter.Export(cancellationEvents, recoveryEvents);

        Assert.Contains("reason=redacted", export, StringComparison.Ordinal);
        Assert.Contains("health event redacted", export, StringComparison.Ordinal);
        Assert.DoesNotContain("exception", export, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", export, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("database", export, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("host", export, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Export_is_bounded_for_large_event_history()
    {
        var cancellationEvents = Enumerable.Range(1, 40)
            .Select(sequence => new RefreshCancellationEvent(sequence, DateTimeOffset.UtcNow, RefreshCancellationReason.Unloaded));
        var recoveryEvents = Enumerable.Range(1, 40)
            .Select(failures => new SiteHealthSummaryRecoveryEvent(DateTimeOffset.UtcNow, "failure", failures));

        var export = SiteHealthSummaryTimelineExporter.Export(cancellationEvents, recoveryEvents);

        Assert.InRange(export.Length, 0, SiteHealthSummaryTimelineExporter.MaxExportLength);
    }

    [Fact]
    public void Export_reports_empty_timeline_without_sensitive_placeholders()
    {
        var export = SiteHealthSummaryTimelineExporter.Export(
            Array.Empty<RefreshCancellationEvent>(),
            Array.Empty<SiteHealthSummaryRecoveryEvent>());

        Assert.Contains("AegisWAF Multi-site recovery timeline", export, StringComparison.Ordinal);
        Assert.Contains("no events", export, StringComparison.Ordinal);
        Assert.DoesNotContain("exception", export, StringComparison.OrdinalIgnoreCase);
    }
}
