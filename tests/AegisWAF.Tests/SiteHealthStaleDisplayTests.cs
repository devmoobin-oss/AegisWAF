namespace AegisWAF.Tests;

public sealed class SiteHealthStaleDisplayTests
{
    [Fact]
    public void Stale_unknown_live_state_does_not_display_an_old_critical_as_current_crisis()
    {
        var state = AegisWAF.Client.SiteHealthDisplayState.Resolve(
            liveHealth: "unknown",
            historicalAlert: "critical",
            recoveryState: "none",
            pendingRestart: false,
            stale: true);

        Assert.Equal("stale", state);
    }
}
