using AegisWAF.Gateway.Sites;

namespace AegisWAF.Tests;

public sealed class SiteHealthSummaryFailureTelemetryTests
{
    [Fact]
    public void Failure_increments_total_and_consecutive_counters_without_site_data()
    {
        var telemetry = new SiteHealthSummaryFailureTelemetry();

        var snapshot = telemetry.RecordFailure();
        snapshot = telemetry.RecordFailure();

        Assert.Equal(2, snapshot.TotalFailures);
        Assert.Equal(2, snapshot.ConsecutiveFailures);
        Assert.Null(snapshot.LastSuccessAt);
    }

    [Fact]
    public void Success_resets_consecutive_failures_but_keeps_total_for_operator_context()
    {
        var telemetry = new SiteHealthSummaryFailureTelemetry();
        telemetry.RecordFailure();
        telemetry.RecordFailure();

        var snapshot = telemetry.RecordSuccess();

        Assert.Equal(2, snapshot.TotalFailures);
        Assert.Equal(0, snapshot.ConsecutiveFailures);
        Assert.NotNull(snapshot.LastSuccessAt);
    }

    [Fact]
    public void Failure_counter_saturates_instead_of_wrapping()
    {
        var telemetry = new SiteHealthSummaryFailureTelemetry(long.MaxValue - 1);

        telemetry.RecordFailure();
        var snapshot = telemetry.RecordFailure();

        Assert.Equal(long.MaxValue, snapshot.TotalFailures);
    }
}
