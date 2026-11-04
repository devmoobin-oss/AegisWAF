using AegisWAF.Client;

namespace AegisWAF.Tests;

public sealed class SiteHealthSummaryRefreshLifecycleTests
{
    [Fact]
    public void Starting_a_new_refresh_cancels_the_previous_refresh()
    {
        using var lifecycle = new SiteHealthSummaryRefreshLifecycle();

        var first = lifecycle.Begin();
        var second = lifecycle.Begin();

        Assert.True(first.IsCancellationRequested);
        Assert.False(second.IsCancellationRequested);
        Assert.True(lifecycle.IsCurrent(second));
    }

    [Fact]
    public void Cancel_invalidates_the_current_refresh_without_affecting_a_future_refresh()
    {
        using var lifecycle = new SiteHealthSummaryRefreshLifecycle();
        var current = lifecycle.Begin();

        lifecycle.Cancel();

        Assert.True(current.IsCancellationRequested);
        Assert.False(lifecycle.IsCurrent(current));
        var future = lifecycle.Begin();
        Assert.False(future.IsCancellationRequested);
        Assert.True(lifecycle.IsCurrent(future));
    }

    [Fact]
    public void Newer_generation_wins_and_generation_does_not_wrap()
    {
        using var lifecycle = new SiteHealthSummaryRefreshLifecycle();

        lifecycle.Begin();
        var first = lifecycle.CurrentGeneration;
        lifecycle.Begin();
        var second = lifecycle.CurrentGeneration;

        Assert.True(second > first);
        Assert.False(lifecycle.IsCurrentGeneration(first));
        Assert.True(lifecycle.IsCurrentGeneration(second));

        using var saturated = new SiteHealthSummaryRefreshLifecycle(long.MaxValue);
        saturated.Begin();
        Assert.Equal(long.MaxValue, saturated.CurrentGeneration);
    }

    [Fact]
    public void Active_refresh_cancellation_records_reason_but_completed_refresh_does_not_create_false_events()
    {
        using var lifecycle = new SiteHealthSummaryRefreshLifecycle();
        var first = lifecycle.Begin();
        lifecycle.Complete(first);
        lifecycle.Begin(RefreshCancellationReason.Retry);
        Assert.Equal(0, lifecycle.CancellationCount);

        lifecycle.Begin(RefreshCancellationReason.Superseded);

        Assert.Equal(1, lifecycle.CancellationCount);
        Assert.Equal(RefreshCancellationReason.Superseded, lifecycle.LastCancellationReason);
        Assert.Single(lifecycle.CancellationHistory);
    }

    [Fact]
    public void Unload_and_retry_reasons_are_closed_and_history_is_bounded()
    {
        using var lifecycle = new SiteHealthSummaryRefreshLifecycle();
        for (var i = 0; i < SiteHealthSummaryRefreshLifecycle.MaxCancellationHistory + 2; i++)
        {
            lifecycle.Begin();
            lifecycle.Invalidate(i % 2 == 0 ? RefreshCancellationReason.Unloaded : RefreshCancellationReason.Retry);
        }

        Assert.Equal(SiteHealthSummaryRefreshLifecycle.MaxCancellationHistory, lifecycle.CancellationHistory.Count);
        Assert.Contains(lifecycle.CancellationHistory, item => item.Reason == RefreshCancellationReason.Unloaded);
        Assert.Contains(lifecycle.CancellationHistory, item => item.Reason == RefreshCancellationReason.Retry);
    }

    [Fact]
    public void Cancellation_history_filter_is_typed_and_bounded()
    {
        using var lifecycle = new SiteHealthSummaryRefreshLifecycle();
        for (var i = 0; i < SiteHealthSummaryRefreshLifecycle.MaxCancellationHistory + 2; i++)
        {
            lifecycle.Begin();
            lifecycle.Invalidate(i % 2 == 0 ? RefreshCancellationReason.Unloaded : RefreshCancellationReason.Retry);
        }

        var all = lifecycle.GetCancellationHistory();
        var unloaded = lifecycle.GetCancellationHistory(RefreshCancellationReason.Unloaded);

        Assert.InRange(all.Count, 1, SiteHealthSummaryRefreshLifecycle.MaxCancellationHistory);
        Assert.NotEmpty(unloaded);
        Assert.All(unloaded, item => Assert.Equal(RefreshCancellationReason.Unloaded, item.Reason));
    }

    [Fact]
    public void Sequence_preserves_event_order_when_clock_moves_backward()
    {
        var times = new Queue<DateTimeOffset>([
            new DateTimeOffset(2026, 10, 26, 2, 0, 2, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 26, 2, 0, 1, TimeSpan.Zero)
        ]);
        using var lifecycle = new SiteHealthSummaryRefreshLifecycle(clock: () => times.Dequeue());

        lifecycle.Begin();
        lifecycle.Invalidate(RefreshCancellationReason.Unloaded);
        lifecycle.Begin();
        lifecycle.Invalidate(RefreshCancellationReason.Retry);

        var events = lifecycle.GetCancellationHistory();

        Assert.Equal(2, events.Count);
        Assert.True(events[0].OccurredAt > events[1].OccurredAt);
        Assert.Equal(1, events[0].Sequence);
        Assert.Equal(2, events[1].Sequence);
        Assert.Equal(RefreshCancellationReason.Unloaded, events[0].Reason);
        Assert.Equal(RefreshCancellationReason.Retry, events[1].Reason);
    }
}
