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

}
