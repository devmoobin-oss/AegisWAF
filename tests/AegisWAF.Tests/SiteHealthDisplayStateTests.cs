namespace AegisWAF.Tests;

public sealed class SiteHealthDisplayStateTests
{
    [Fact]
    public void Healthy_live_state_overrides_historical_critical_as_recovered()
    {
        var state = AegisWAF.Client.SiteHealthDisplayState.Resolve(
            liveHealth: "healthy",
            historicalAlert: "critical",
            recoveryState: "none",
            pendingRestart: false);

        Assert.Equal("recovered", state);
    }

    [Fact]
    public void Pending_restart_has_priority_over_live_and_historical_states()
    {
        var state = AegisWAF.Client.SiteHealthDisplayState.Resolve(
            liveHealth: "healthy",
            historicalAlert: "critical",
            recoveryState: "recovered",
            pendingRestart: true);

        Assert.Equal("pending-restart", state);
    }

    [Fact]
    public void Unhealthy_live_state_does_not_hide_a_historical_critical_alert()
    {
        var state = AegisWAF.Client.SiteHealthDisplayState.Resolve(
            liveHealth: "unhealthy",
            historicalAlert: "critical",
            recoveryState: "none",
            pendingRestart: false);

        Assert.Equal("critical", state);
    }

    [Fact]
    public void Unhealthy_live_state_overrides_a_historical_healthy_state()
    {
        var state = AegisWAF.Client.SiteHealthDisplayState.Resolve(
            liveHealth: "unhealthy",
            historicalAlert: "healthy",
            recoveryState: "none",
            pendingRestart: false);

        Assert.Equal("unhealthy", state);
    }

    [Fact]
    public void Unknown_live_state_uses_historical_alert_when_available()
    {
        var state = AegisWAF.Client.SiteHealthDisplayState.Resolve(
            liveHealth: "unknown",
            historicalAlert: "degraded",
            recoveryState: "none",
            pendingRestart: false);

        Assert.Equal("degraded", state);
    }
}
