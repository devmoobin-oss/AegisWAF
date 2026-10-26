using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace AegisWAF.Tests;

public sealed class SecurityEventBatchingTests
{
    [Fact]
    public async Task Bounded_security_event_queue_reports_backpressure_and_reads_a_batch()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Waf:Events:QueueCapacity"] = "2"
            })
            .Build();
        var events = new SecurityEvents(NullLogger<SecurityEvents>.Instance, configuration);

        Assert.True(events.TryWrite(Event("one")));
        Assert.True(events.TryWrite(Event("two")));
        Assert.False(events.TryWrite(Event("three")));
        Assert.Equal(2, events.QueueDepth);
        Assert.Equal(1, events.Dropped);

        var batch = await events.ReadBatchAsync(10, TimeSpan.FromMilliseconds(10), CancellationToken.None);

        Assert.Equal(["one", "two"], batch.Select(x => x.Rule).ToArray());
        Assert.Equal(0, events.QueueDepth);
    }

    [Fact]
    public async Task Batch_wait_is_bounded_when_fewer_events_than_batch_size_are_available()
    {
        var configuration = new ConfigurationBuilder().Build();
        var events = new SecurityEvents(NullLogger<SecurityEvents>.Instance, configuration);
        events.TryWrite(Event("only"));
        var started = Stopwatch.GetTimestamp();

        var batch = await events.ReadBatchAsync(100, TimeSpan.FromMilliseconds(25), CancellationToken.None);

        var elapsed = Stopwatch.GetElapsedTime(started);
        Assert.Single(batch);
        Assert.True(elapsed >= TimeSpan.FromMilliseconds(10), $"batch returned too early: {elapsed}");
        Assert.True(elapsed < TimeSpan.FromSeconds(1), $"batch wait was not bounded: {elapsed}");
    }

    private static SecurityEvent Event(string rule) =>
        new(DateTimeOffset.UtcNow, "127.0.0.1", "GET", "/", rule, 403);
}
