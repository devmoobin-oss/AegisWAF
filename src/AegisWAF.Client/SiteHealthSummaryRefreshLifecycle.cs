namespace AegisWAF.Client;

public enum RefreshCancellationReason
{
    None,
    Superseded,
    Unloaded,
    Retry
}

public sealed record RefreshCancellationEvent(long Sequence, DateTimeOffset OccurredAt, RefreshCancellationReason Reason);

/// <summary>Owns one cancellable local refresh and invalidates superseded work.</summary>
public sealed class SiteHealthSummaryRefreshLifecycle : IDisposable
{
    public const int MaxCancellationHistory = 8;
    private readonly object _gate = new();
    private readonly Queue<RefreshCancellationEvent> _cancellationHistory = new();
    private readonly Func<DateTimeOffset> _clock;
    private CancellationTokenSource? _current;
    private long _generation;
    private long _nextCancellationSequence;
    private long _cancellationCount;
    private RefreshCancellationReason _lastCancellationReason;
    private bool _disposed;

    public SiteHealthSummaryRefreshLifecycle(long initialGeneration = 0, Func<DateTimeOffset>? clock = null, long initialCancellationSequence = 0)
    {
        _generation = Math.Clamp(initialGeneration, 0, long.MaxValue);
        _nextCancellationSequence = Math.Clamp(initialCancellationSequence, 0, long.MaxValue);
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public long CurrentGeneration
    {
        get { lock (_gate) return _generation; }
    }

    public long CancellationCount
    {
        get { lock (_gate) return _cancellationCount; }
    }

    public RefreshCancellationReason LastCancellationReason
    {
        get { lock (_gate) return _lastCancellationReason; }
    }

    public IReadOnlyList<RefreshCancellationEvent> CancellationHistory
    {
        get { return GetCancellationHistory(); }
    }

    public IReadOnlyList<RefreshCancellationEvent> GetCancellationHistory(RefreshCancellationReason reason = RefreshCancellationReason.None)
    {
        lock (_gate)
            return (reason is RefreshCancellationReason.None
                    ? _cancellationHistory
                    : _cancellationHistory.Where(item => item.Reason == reason))
                .OrderBy(item => item.Sequence)
                .ToArray();
    }

    public CancellationToken Begin(RefreshCancellationReason reason = RefreshCancellationReason.Superseded)
    {
        CancellationTokenSource? previous;
        CancellationTokenSource next;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            previous = _current;
            if (previous is not null && !previous.IsCancellationRequested) RecordCancellationUnsafe(reason);
            if (_generation < long.MaxValue) _generation++;
            next = new CancellationTokenSource();
            _current = next;
        }

        previous?.Cancel();
        previous?.Dispose();
        return next.Token;
    }

    public void Complete(CancellationToken token)
    {
        CancellationTokenSource? completed = null;
        lock (_gate)
        {
            if (_current is not null && _current.Token == token)
            {
                completed = _current;
                _current = null;
            }
        }

        completed?.Dispose();
    }

    public void Cancel(RefreshCancellationReason reason = RefreshCancellationReason.Unloaded)
    {
        CancellationTokenSource? current;
        lock (_gate)
        {
            current = _current;
            if (current is not null && !current.IsCancellationRequested) RecordCancellationUnsafe(reason);
        }
        current?.Cancel();
    }

    public bool IsCurrent(CancellationToken token)
    {
        lock (_gate)
            return !_disposed && _current is not null && _current.Token == token && !token.IsCancellationRequested;
    }

    public bool IsCurrentGeneration(long generation)
    {
        lock (_gate) return !_disposed && _generation == generation;
    }

    public void Invalidate(RefreshCancellationReason reason = RefreshCancellationReason.Unloaded)
    {
        CancellationTokenSource? current;
        lock (_gate)
        {
            if (_disposed) return;
            if (_generation < long.MaxValue) _generation++;
            current = _current;
            if (current is not null && !current.IsCancellationRequested) RecordCancellationUnsafe(reason);
        }

        current?.Cancel();
    }

    private void RecordCancellationUnsafe(RefreshCancellationReason reason)
    {
        if (reason is RefreshCancellationReason.None) return;
        if (_cancellationCount < long.MaxValue) _cancellationCount++;
        _lastCancellationReason = reason;
        if (_nextCancellationSequence < long.MaxValue) _nextCancellationSequence++;
        _cancellationHistory.Enqueue(new RefreshCancellationEvent(_nextCancellationSequence, _clock(), reason));
        while (_cancellationHistory.Count > MaxCancellationHistory) _cancellationHistory.Dequeue();
    }

    public void Dispose()
    {
        CancellationTokenSource? current;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            current = _current;
            _current = null;
        }

        current?.Cancel();
        current?.Dispose();
    }
}
