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

    [Fact]
    public void Stale_healthy_no_data_and_recovered_history_are_not_counted_as_current_states()
    {
        var freshHealthy = Item("fresh-healthy", "healthy", "none", isStale: false);
        var staleHealthy = Item("stale-healthy", "healthy", "none", isStale: true);
        var staleNoData = Item("stale-no-data", "no-data", "none", isStale: true);
        var staleRecovered = Item("stale-recovered", "critical", "recovered", isStale: true);

        var counts = SiteHealthSummaryAlertCounts.Calculate([
            freshHealthy,
            staleHealthy,
            staleNoData,
            staleRecovered
        ]);

        Assert.Equal(1, counts.HealthySites);
        Assert.Equal(0, counts.NoDataSites);
        Assert.Equal(0, counts.RecoveredSites);
        Assert.Equal(3, counts.StaleSites);
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
