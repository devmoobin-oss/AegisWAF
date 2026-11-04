using AegisWAF.Gateway.Sites;
using Npgsql;

namespace AegisWAF.Tests;

public sealed class SiteRuntimeReloadRetryTests
{
    [Fact]
    public async Task Retries_transient_reload_failures_with_bounded_exponential_backoff()
    {
        var attempts = 0;
        var delays = new List<TimeSpan>();
        var failures = new List<bool>();

        await SiteRuntimeReloadRetry.RunAsync(
            _ =>
            {
                attempts++;
                if (attempts < 3) throw new NpgsqlException("temporary database failure");
                return Task.CompletedTask;
            },
            (_, willRetry) => failures.Add(willRetry),
            (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal(3, attempts);
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)], delays);
        Assert.Equal([true, true], failures);
    }

    [Fact]
    public async Task Does_not_retry_a_rejected_snapshot_until_a_new_notification_arrives()
    {
        var attempts = 0;
        var delays = new List<TimeSpan>();
        var failures = new List<bool>();

        await SiteRuntimeReloadRetry.RunAsync(
            _ =>
            {
                attempts++;
                throw new SiteRuntimeActivationException("invalid site snapshot");
            },
            (_, willRetry) => failures.Add(willRetry),
            (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal(1, attempts);
        Assert.Empty(delays);
        Assert.Equal([false], failures);
    }

    [Fact]
    public void Backoff_never_exceeds_the_listener_maximum()
    {
        Assert.Equal(TimeSpan.FromSeconds(2), SiteRuntimeReloadRetry.NextDelay(TimeSpan.FromSeconds(1)));
        Assert.Equal(TimeSpan.FromSeconds(30), SiteRuntimeReloadRetry.NextDelay(TimeSpan.FromSeconds(16)));
        Assert.Equal(TimeSpan.FromSeconds(30), SiteRuntimeReloadRetry.NextDelay(TimeSpan.FromSeconds(30)));
    }
}
