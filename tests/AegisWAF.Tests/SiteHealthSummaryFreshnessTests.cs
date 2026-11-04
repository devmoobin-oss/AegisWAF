using AegisWAF.Client;

namespace AegisWAF.Tests;

public sealed class SiteHealthSummaryFreshnessTests
{
    [Fact]
    public void Server_generated_summary_is_stale_at_five_minutes_without_using_local_now()
    {
        var now = new DateTimeOffset(2026, 10, 26, 2, 0, 0, TimeSpan.Zero);
        var state = new SiteHealthSummaryRecoveryState();
        state.RecordSuccess(NewSummary(now.AddMinutes(-5)));

        Assert.True(state.IsFreshnessStale(now));
        Assert.Equal(TimeSpan.FromMinutes(5), state.LastSuccessfulSummaryAge(now));
    }

    [Fact]
    public void Future_or_missing_server_timestamp_is_not_declared_stale_by_age()
    {
        var now = new DateTimeOffset(2026, 10, 26, 2, 0, 0, TimeSpan.Zero);
        var future = new SiteHealthSummaryRecoveryState();
        future.RecordSuccess(NewSummary(now.AddMinutes(1)));
        var missing = new SiteHealthSummaryRecoveryState();
        missing.RecordSuccess(NewSummary(null));

        Assert.False(future.IsFreshnessStale(now));
        Assert.Null(future.LastSuccessfulSummaryAge(now));
        Assert.False(missing.IsFreshnessStale(now));
        Assert.Null(missing.LastSuccessfulSummaryAge(now));
    }

    private static SiteHealthSummaryDto NewSummary(DateTimeOffset? generatedAt) => new(
        WindowSize: 20,
        TotalSites: 1,
        CriticalSites: 0,
        DegradedSites: 0,
        HealthySites: 1,
        NoDataSites: 0,
        StaleSites: 0,
        Sites: Array.Empty<SiteHealthComparisonDto>(),
        GeneratedAt: generatedAt);
}
