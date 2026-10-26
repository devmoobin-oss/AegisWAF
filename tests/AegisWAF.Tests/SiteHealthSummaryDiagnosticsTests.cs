using AegisWAF.Client;

namespace AegisWAF.Tests;

public sealed class SiteHealthSummaryDiagnosticsTests
{
    [Fact]
    public void Refresh_correlation_is_opaque_and_changes_per_refresh()
    {
        var state = new SiteHealthSummaryRecoveryState();

        var first = state.BeginRefresh();
        var second = state.BeginRefresh();

        Assert.Matches("^[0-9a-f]{8}$", first);
        Assert.Matches("^[0-9a-f]{8}$", second);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Diagnostics_are_bounded_and_exclude_infrastructure_details()
    {
        var state = new SiteHealthSummaryRecoveryState();
        state.BeginRefresh();
        state.RecordFailure();

        var diagnostics = state.BuildDiagnostics(new DateTimeOffset(2026, 10, 26, 2, 0, 0, TimeSpan.Zero), RefreshCancellationReason.Unloaded);

        Assert.InRange(diagnostics.Length, 1, 256);
        Assert.Contains("summary-refresh:", diagnostics);
        Assert.Contains("failures:1", diagnostics);
        Assert.Contains("cancel:unloaded", diagnostics);
        Assert.DoesNotContain("exception", diagnostics, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("database", diagnostics, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("host", diagnostics, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sql", diagnostics, StringComparison.OrdinalIgnoreCase);
    }
}
