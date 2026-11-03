using AegisWAF.Gateway.Sites;

namespace AegisWAF.Tests;

public sealed class SiteHealthTrendTests
{
    [Fact]
    public void Empty_history_reports_no_data_without_a_false_healthy_signal()
    {
        var trend = SiteHealthTrendCalculator.Calculate([], 20);

        Assert.Equal(0, trend.Total);
        Assert.Equal("no-data", trend.AlertState);
        Assert.Equal("none", trend.RecoveryState);
        Assert.Equal(0, trend.HealthyPercent);
        Assert.Null(trend.LastCheckedAt);
    }

    [Fact]
    public void Mixed_history_reports_counts_percentage_and_newest_check()
    {
        var newest = DateTimeOffset.UtcNow;
        var observations = new SiteHealthObservation[]
        {
            new("primary", "healthy", null, newest),
            new("primary", "unknown", "timeout", newest.AddMinutes(-1)),
            new("backup", "unhealthy", "502", newest.AddMinutes(-2)),
            new("backup", "healthy", null, newest.AddMinutes(-3))
        };

        var trend = SiteHealthTrendCalculator.Calculate(observations, 4);

        Assert.Equal(4, trend.Total);
        Assert.Equal(2, trend.Healthy);
        Assert.Equal(1, trend.Unhealthy);
        Assert.Equal(1, trend.Unknown);
        Assert.Equal(50d, trend.HealthyPercent);
        Assert.Equal(0, trend.ConsecutiveUnhealthy);
        Assert.Equal("degraded", trend.AlertState);
        Assert.Equal("none", trend.RecoveryState);
        Assert.Equal(newest, trend.LastCheckedAt);
    }

    [Fact]
    public void Newest_healthy_after_historical_failures_reports_recovered_without_erasing_history()
    {
        var newest = DateTimeOffset.UtcNow;
        var observations = new SiteHealthObservation[]
        {
            new("primary", "healthy", null, newest),
            new("primary", "unhealthy", "timeout", newest.AddMinutes(-1)),
            new("primary", "unknown", "no_data", newest.AddMinutes(-2))
        };

        var trend = SiteHealthTrendCalculator.Calculate(observations, 20);

        Assert.Equal("degraded", trend.AlertState);
        Assert.Equal("recovered", trend.RecoveryState);
        Assert.Equal(1, trend.Healthy);
        Assert.Equal(1, trend.Unhealthy);
        Assert.Equal(1, trend.Unknown);
    }

    [Fact]
    public void Recovery_is_none_when_latest_observation_is_not_healthy_or_history_is_clean()
    {
        var newest = DateTimeOffset.UtcNow;
        var latestUnhealthy = SiteHealthTrendCalculator.Calculate(
            [new SiteHealthObservation("primary", "unhealthy", "timeout", newest), new SiteHealthObservation("primary", "healthy", null, newest.AddMinutes(-1))], 20);
        var allHealthy = SiteHealthTrendCalculator.Calculate(
            [new SiteHealthObservation("primary", "healthy", null, newest), new SiteHealthObservation("primary", "healthy", null, newest.AddMinutes(-1))], 20);

        Assert.Equal("none", latestUnhealthy.RecoveryState);
        Assert.Equal("none", allHealthy.RecoveryState);
    }

    [Fact]
    public void Trend_ignores_history_for_upstreams_removed_from_the_current_site_topology()
    {
        var newest = DateTimeOffset.UtcNow;
        var observations = new SiteHealthObservation[]
        {
            new("primary", "healthy", null, newest),
            new("retired-backup", "unhealthy", "timeout", newest.AddMinutes(-1))
        };

        var currentUpstreams = new[] { "primary" };
        var filtered = SiteHealthTrendCalculator.FilterToCurrentUpstreams(observations, currentUpstreams);
        var trend = SiteHealthTrendCalculator.Calculate(filtered, 20, expectedUpstreamCount: currentUpstreams.Length);

        Assert.Single(filtered);
        Assert.Equal("primary", filtered[0].UpstreamId);
        Assert.Equal("healthy", trend.AlertState);
        Assert.Equal(100d, trend.HealthyPercent);
        Assert.Equal(1, trend.Total);
    }

    [Fact]
    public void Trend_filter_normalizes_current_upstream_ids()
    {
        var now = DateTimeOffset.UtcNow;
        var observations = new SiteHealthObservation[]
        {
            new("primary", "healthy", null, now),
            new("retired-backup", "unhealthy", "timeout", now.AddMinutes(-1))
        };

        var filtered = SiteHealthTrendCalculator.FilterToCurrentUpstreams(observations, [" PRIMARY "]);

        Assert.Single(filtered);
        Assert.Equal("primary", filtered[0].UpstreamId);
    }

    [Fact]
    public void Trend_does_not_count_whitespace_variants_as_distinct_upstreams()
    {
        var now = DateTimeOffset.UtcNow;
        var observations = new SiteHealthObservation[]
        {
            new("primary", "unhealthy", "timeout", now),
            new(" primary ", "unhealthy", "timeout", now)
        };

        var trend = SiteHealthTrendCalculator.Calculate(observations, 20, expectedUpstreamCount: 2);

        Assert.Equal(0, trend.ConsecutiveUnhealthy);
        Assert.Equal("degraded", trend.AlertState);
    }

    [Fact]
    public void Three_newest_unhealthy_observations_raise_a_critical_alert()
    {
        var newest = DateTimeOffset.UtcNow;
        var observations = Enumerable.Range(0, 4)
            .Select(i => new SiteHealthObservation("primary", i < 3 ? "unhealthy" : "healthy", "timeout", newest.AddMinutes(-i)))
            .ToArray();

        var trend = SiteHealthTrendCalculator.Calculate(observations, 64);

        Assert.Equal(3, trend.ConsecutiveUnhealthy);
        Assert.Equal("critical", trend.AlertState);
    }

    [Fact]
    public void Multiple_upstreams_in_one_failed_batch_count_as_one_failure_round()
    {
        var newest = DateTimeOffset.UtcNow;
        var observations = new SiteHealthObservation[]
        {
            new("primary", "unhealthy", "timeout", newest),
            new("backup", "unhealthy", "timeout", newest),
            new("primary", "healthy", null, newest.AddMinutes(-1)),
            new("backup", "healthy", null, newest.AddMinutes(-1))
        };

        var trend = SiteHealthTrendCalculator.Calculate(observations, 20, expectedUpstreamCount: 2);

        Assert.Equal(1, trend.ConsecutiveUnhealthy);
        Assert.Equal("degraded", trend.AlertState);
    }

    [Fact]
    public void Three_complete_all_upstream_failed_batches_raise_critical()
    {
        var newest = DateTimeOffset.UtcNow;
        var observations = Enumerable.Range(0, 3)
            .SelectMany(i => new[]
            {
                new SiteHealthObservation("primary", "unhealthy", "timeout", newest.AddSeconds(-i * 10)),
                new SiteHealthObservation("backup", "unhealthy", "timeout", newest.AddSeconds(-i * 10))
            })
            .ToArray();

        var trend = SiteHealthTrendCalculator.Calculate(observations, 20, expectedUpstreamCount: 2);

        Assert.Equal(3, trend.ConsecutiveUnhealthy);
        Assert.Equal("critical", trend.AlertState);
    }

    [Fact]
    public void Partial_failure_stays_degraded_even_after_three_batches()
    {
        var newest = DateTimeOffset.UtcNow;
        var observations = Enumerable.Range(0, 3)
            .SelectMany(i => new[]
            {
                new SiteHealthObservation("primary", "unhealthy", "timeout", newest.AddSeconds(-i * 10)),
                new SiteHealthObservation("backup", "healthy", null, newest.AddSeconds(-i * 10))
            })
            .ToArray();

        var trend = SiteHealthTrendCalculator.Calculate(observations, 20, expectedUpstreamCount: 2);

        Assert.Equal(0, trend.ConsecutiveUnhealthy);
        Assert.Equal("degraded", trend.AlertState);
    }

    [Fact]
    public void Incomplete_failed_batch_does_not_become_critical()
    {
        var newest = DateTimeOffset.UtcNow;
        var observations = Enumerable.Range(0, 3)
            .Select(i => new SiteHealthObservation("primary", "unhealthy", "timeout", newest.AddSeconds(-i * 10)))
            .ToArray();

        var trend = SiteHealthTrendCalculator.Calculate(observations, 20, expectedUpstreamCount: 2);

        Assert.Equal(0, trend.ConsecutiveUnhealthy);
        Assert.Equal("degraded", trend.AlertState);
    }

    [Fact]
    public void Sub_second_timestamp_skew_is_treated_as_one_batch()
    {
        var now = DateTimeOffset.UtcNow;
        var newest = new DateTimeOffset(now.UtcDateTime.Ticks - now.UtcDateTime.Ticks % TimeSpan.TicksPerSecond, TimeSpan.Zero);
        var observations = new SiteHealthObservation[]
        {
            new("primary", "unhealthy", "timeout", newest),
            new("backup", "unhealthy", "timeout", newest.AddMilliseconds(400))
        };

        var trend = SiteHealthTrendCalculator.Calculate(observations, 20, expectedUpstreamCount: 2);

        Assert.Equal(1, trend.ConsecutiveUnhealthy);
        Assert.Equal("degraded", trend.AlertState);
    }

    [Fact]
    public void Live_healthy_status_can_coexist_with_a_critical_historical_trend()
    {
        var newest = DateTimeOffset.UtcNow;
        var historical = Enumerable.Range(0, 4)
            .Select(i => new SiteHealthObservation("primary", "unhealthy", "timeout", newest.AddMinutes(-i)))
            .ToArray();

        var trend = SiteHealthTrendCalculator.Calculate(historical, 20);
        var liveStatus = SiteProxyHealth.Status("Healthy", "Unknown");

        Assert.Equal("healthy", liveStatus);
        Assert.Equal("critical", trend.AlertState);
        Assert.Equal(0d, trend.HealthyPercent);
        Assert.Equal(4, trend.ConsecutiveUnhealthy);
    }

    [Fact]
    public void Trend_window_is_bounded_to_the_persisted_retention()
    {
        var observations = Enumerable.Range(0, 80)
            .Select(i => new SiteHealthObservation("primary", "healthy", null, DateTimeOffset.UtcNow.AddSeconds(-i)))
            .ToArray();

        var trend = SiteHealthTrendCalculator.Calculate(observations, 1000);

        Assert.Equal(64, trend.Total);
    }
}
