using AegisWAF.Gateway.Sites;

namespace AegisWAF.Tests;

public sealed class SiteHealthSummaryRecoveryRankingTests
{
    [Fact]
    public void Recovered_history_does_not_outrank_current_actionable_alerts()
    {
        var now = DateTimeOffset.UtcNow;
        var ordered = SiteHealthComparisonCalculator.Calculate(
            [
                ("a-recovered", (IReadOnlyList<SiteHealthObservation>)[
                    new("primary", "healthy", null, now),
                    new("primary", "unhealthy", "timeout", now.AddMinutes(-1))]),
                ("z-current", (IReadOnlyList<SiteHealthObservation>)[
                    new("primary", "unhealthy", "timeout", now)])
            ],
            window: 20,
            limit: 10,
            now,
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["a-recovered"] = 1,
                ["z-current"] = 1
            });

        Assert.Equal("z-current", ordered[0].SiteId);
        Assert.Equal("a-recovered", ordered[1].SiteId);
    }

    [Fact]
    public void Current_actionable_alerts_outrank_stale_alerts_of_the_same_severity()
    {
        var now = DateTimeOffset.UtcNow;
        var ordered = SiteHealthComparisonCalculator.Calculate(
            [
                ("stale-critical", (IReadOnlyList<SiteHealthObservation>)[
                    new("primary", "unhealthy", "timeout", now.AddMinutes(-30))]),
                ("current-critical", (IReadOnlyList<SiteHealthObservation>)[
                    new("primary", "unhealthy", "timeout", now)])
            ],
            window: 20,
            limit: 10,
            now,
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["stale-critical"] = 1,
                ["current-critical"] = 1
            });

        Assert.Equal("current-critical", ordered[0].SiteId);
        Assert.Equal("stale-critical", ordered[1].SiteId);
        Assert.True(ordered[1].IsStale);
    }

}
