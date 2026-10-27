using AegisWAF.Gateway.Sites;

namespace AegisWAF.Tests;

public sealed class SiteHealthSummaryAlertCountsTests
{
    [Fact]
    public void Stale_alerts_are_separated_from_current_critical_and_degraded_counts()
    {
        var currentCritical = Item("current-critical", "critical", "none", isStale: false);
        var staleCritical = Item("stale-critical", "critical", "none", isStale: true);
        var staleDegraded = Item("stale-degraded", "degraded", "none", isStale: true);
        var recovered = Item("recovered", "degraded", "recovered", isStale: false);

        var counts = SiteHealthSummaryAlertCounts.Calculate([
            currentCritical,
            staleCritical,
            staleDegraded,
            recovered
        ]);

        Assert.Equal(1, counts.CriticalSites);
        Assert.Equal(0, counts.DegradedSites);
        Assert.Equal(2, counts.StaleSites);
        Assert.Equal(1, counts.StaleCriticalSites);
        Assert.Equal(1, counts.StaleDegradedSites);
        Assert.Equal(1, counts.RecoveredSites);
    }

    [Fact]
    public void Recovered_critical_history_is_not_counted_as_a_current_critical_site()
    {
        var recoveredCritical = Item("recovered-critical", "critical", "recovered", isStale: false);

        var counts = SiteHealthSummaryAlertCounts.Calculate([recoveredCritical]);

        Assert.Equal(0, counts.CriticalSites);
        Assert.Equal(1, counts.RecoveredSites);
    }

    private static SiteHealthComparisonItem Item(string siteId, string alertState, string recoveryState, bool isStale) =>
        new(siteId, new SiteHealthTrend(
            WindowSize: 20,
            Total: 1,
            Healthy: 0,
            Unhealthy: 1,
            Unknown: 0,
            HealthyPercent: 0,
            ConsecutiveUnhealthy: 3,
            AlertState: alertState,
            LastCheckedAt: DateTimeOffset.UtcNow,
            RecoveryState: recoveryState), isStale);
}
