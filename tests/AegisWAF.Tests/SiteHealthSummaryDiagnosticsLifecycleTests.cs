using AegisWAF.Client;

namespace AegisWAF.Tests;

public sealed class SiteHealthSummaryDiagnosticsLifecycleTests
{
    [Fact]
    public void Starting_a_new_refresh_invalidates_the_previous_diagnostics()
    {
        var state = new SiteHealthSummaryRecoveryState();
        state.BeginRefresh();
        state.RecordFailure();
        state.BuildDiagnostics(DateTimeOffset.UtcNow);
        Assert.True(state.DiagnosticsAvailable);

        state.BeginRefresh();

        Assert.False(state.DiagnosticsAvailable);
    }

    [Fact]
    public void Diagnostics_are_bounded_for_long_lived_refresh_sessions()
    {
        var state = new SiteHealthSummaryRecoveryState();
        state.BeginRefresh();
        state.RecordFailure();

        var diagnostics = state.BuildDiagnostics(DateTimeOffset.UtcNow);

        Assert.InRange(diagnostics.Length, 1, SiteHealthSummaryRecoveryState.MaxDiagnosticsLength);
    }
}
