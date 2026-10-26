using AegisWAF.Gateway.Sites;

namespace AegisWAF.Tests;

public sealed class SiteHealthAlertTests
{
    [Fact]
    public void Cooldown_validation_accepts_bounds_and_rejects_outside_them()
    {
        Assert.True(HealthAlertLifecycle.TryValidateCooldown(60, out _));
        Assert.True(HealthAlertLifecycle.TryValidateCooldown(86_400, out _));
        Assert.False(HealthAlertLifecycle.TryValidateCooldown(59, out var tooShort));
        Assert.False(HealthAlertLifecycle.TryValidateCooldown(86_401, out var tooLong));
        Assert.NotNull(tooShort);
        Assert.NotNull(tooLong);
    }

    [Fact]
    public void Only_degraded_and_critical_states_are_actionable()
    {
        Assert.True(HealthAlertLifecycle.IsActionable("degraded"));
        Assert.True(HealthAlertLifecycle.IsActionable("critical"));
        Assert.False(HealthAlertLifecycle.IsActionable("healthy"));
        Assert.False(HealthAlertLifecycle.IsActionable("no-data"));
        Assert.False(HealthAlertLifecycle.IsActionable(new SiteHealthTrend(20, 3, 1, 1, 1, 33.33, 0, "degraded", DateTimeOffset.UtcNow, "recovered")));
    }

    [Fact]
    public void Suppression_requires_matching_state_and_future_expiry()
    {
        var now = DateTimeOffset.UtcNow;
        var until = now.AddMinutes(5);
        Assert.True(HealthAlertLifecycle.IsSuppressed("critical", "critical", until, now));
        Assert.False(HealthAlertLifecycle.IsSuppressed("degraded", "critical", until, now));
        Assert.False(HealthAlertLifecycle.IsSuppressed("critical", "critical", now, now));
    }

    [Fact]
    public void Comparison_is_bounded_sorted_by_severity_and_marks_stale_sites()
    {
        var now = DateTimeOffset.UtcNow;
        var inputs = new[]
        {
            ("healthy-site", (IReadOnlyList<SiteHealthObservation>)[new("primary", "healthy", null, now)]),
            ("critical-site", (IReadOnlyList<SiteHealthObservation>)Enumerable.Range(0, 3).Select(i => new SiteHealthObservation("primary", "unhealthy", "timeout", now.AddMinutes(-i))).ToArray()),
            ("stale-site", (IReadOnlyList<SiteHealthObservation>)[new("primary", "healthy", null, now.AddHours(-1))])
        };

        var comparison = SiteHealthComparisonCalculator.Calculate(inputs, 20, 2, now);

        Assert.Equal(2, comparison.Count);
        Assert.Equal("critical-site", comparison[0].SiteId);
        Assert.Equal("stale-site", comparison[1].SiteId);
        Assert.True(comparison[1].IsStale);
    }
}
