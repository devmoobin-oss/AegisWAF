using AegisWAF.Client;

namespace AegisWAF.Tests;

public sealed class SiteHealthSummaryRecoveryStateTests
{
    [Fact]
    public void Repeated_failures_mark_last_successful_summary_stale_at_deterministic_threshold()
    {
        var state = new SiteHealthSummaryRecoveryState();
        var summary = NewSummary();

        state.RecordSuccess(summary);
        state.RecordFailure();
        Assert.False(state.IsStale);

        state.RecordFailure();
        Assert.True(state.IsStale);
        Assert.Same(summary, state.LastSuccessfulSummary);
    }

    [Fact]
    public void Event_history_is_bounded_and_success_records_recovery_without_sensitive_data()
    {
        var state = new SiteHealthSummaryRecoveryState();
        state.RecordSuccess(NewSummary());
        for (var i = 0; i < SiteHealthSummaryRecoveryState.MaxEventHistory + 3; i++) state.RecordFailure();

        state.RecordSuccess(NewSummary());

        Assert.Equal(SiteHealthSummaryRecoveryState.MaxEventHistory, state.Events.Count);
        Assert.Equal("recovered", state.Events[^1].Kind);
        Assert.Equal(0, state.ConsecutiveFailures);
        Assert.All(state.Events, item => Assert.Contains(item.Kind, new[] { "failure", "recovered" }));
        Assert.All(state.Events, item => Assert.DoesNotContain("exception", item.Display, StringComparison.OrdinalIgnoreCase));
        Assert.All(state.Events, item => Assert.DoesNotContain("database", item.Display, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Success_without_failures_does_not_create_recovery_noise()
    {
        var state = new SiteHealthSummaryRecoveryState();

        state.RecordSuccess(NewSummary());
        state.RecordSuccess(NewSummary());

        Assert.Equal(0, state.ConsecutiveFailures);
        Assert.Empty(state.Events);
    }

    private static SiteHealthSummaryDto NewSummary() => new(
        WindowSize: 20,
        TotalSites: 1,
        CriticalSites: 0,
        DegradedSites: 0,
        HealthySites: 1,
        NoDataSites: 0,
        StaleSites: 0,
        Sites: Array.Empty<SiteHealthComparisonDto>());
}
