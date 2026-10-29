namespace AegisWAF.Gateway.Sites;

public sealed record SiteHealthObservation(
    string UpstreamId,
    string Health,
    string? Reason,
    DateTimeOffset CheckedAt);

/// <summary>Bounded process-local health history. It deliberately omits upstream addresses and credentials.</summary>
public sealed class SiteHealthHistory
{
    public const int DefaultMaxSites = 1_024;
    private readonly object _gate = new();
    private readonly int _maxObservationsPerSite;
    private readonly int _maxSites;
    private readonly Dictionary<string, HistoryBucket> _observations = new(StringComparer.OrdinalIgnoreCase);
    private long _touchSequence;

    public SiteHealthHistory(int maxObservationsPerSite = 64, int maxSites = DefaultMaxSites)
    {
        if (maxObservationsPerSite is < 1 or > 256) throw new ArgumentOutOfRangeException(nameof(maxObservationsPerSite));
        if (maxSites is < 1 or > 100_000) throw new ArgumentOutOfRangeException(nameof(maxSites));
        _maxObservationsPerSite = maxObservationsPerSite;
        _maxSites = maxSites;
    }

    public void Record(string siteId, IReadOnlyList<SiteHealthProbeResult> results)
    {
        if (string.IsNullOrWhiteSpace(siteId) || results.Count == 0) return;
        lock (_gate)
        {
            if (!_observations.TryGetValue(siteId, out var bucket))
            {
                if (_observations.Count >= _maxSites)
                {
                    var leastRecentlyUsed = _observations
                        .MinBy(x => x.Value.LastTouched)
                        .Key;
                    _observations.Remove(leastRecentlyUsed);
                }

                _observations[siteId] = bucket = new HistoryBucket(_maxObservationsPerSite);
            }

            bucket.LastTouched = NextTouch();
            foreach (var result in results)
            {
                bucket.Observations.Enqueue(new SiteHealthObservation(result.UpstreamId, result.Health, SanitizeReason(result.Reason), result.CheckedAt));
                while (bucket.Observations.Count > _maxObservationsPerSite) bucket.Observations.Dequeue();
            }
        }
    }

    public IReadOnlyList<SiteHealthObservation> Read(string siteId, int limit = 20)
    {
        limit = Math.Clamp(limit, 1, 100);
        lock (_gate)
        {
            if (!_observations.TryGetValue(siteId, out var bucket)) return [];
            bucket.LastTouched = NextTouch();
            return bucket.Observations.Reverse().Take(limit).ToArray();
        }
    }

    /// <summary>
    /// Combines durable observations with the bounded process-local fallback.
    /// A probe can be present in both sources after PostgreSQL recovers, so the
    /// same observation must only be exposed once.
    /// </summary>
    public static IReadOnlyList<SiteHealthObservation> Merge(
        IReadOnlyList<SiteHealthObservation> persisted,
        IReadOnlyList<SiteHealthObservation> processMemory,
        int limit = 20)
    {
        ArgumentNullException.ThrowIfNull(persisted);
        ArgumentNullException.ThrowIfNull(processMemory);
        limit = Math.Clamp(limit, 1, 100);

        return persisted
            .Concat(processMemory)
            .GroupBy(x => new ObservationKey(
                x.UpstreamId.Trim(),
                x.Health.Trim(),
                x.Reason?.Trim(),
                x.CheckedAt))
            .Select(group => group.First())
            .OrderByDescending(x => x.CheckedAt)
            .Take(limit)
            .ToArray();
    }

    private long NextTouch() => _touchSequence == long.MaxValue ? long.MaxValue : ++_touchSequence;

    private sealed class HistoryBucket(int capacity)
    {
        public Queue<SiteHealthObservation> Observations { get; } = new(capacity);
        public long LastTouched { get; set; }
    }

    private sealed record ObservationKey(string UpstreamId, string Health, string? Reason, DateTimeOffset CheckedAt)
    {
        public bool Equals(ObservationKey? other) => other is not null
            && string.Equals(UpstreamId, other.UpstreamId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Health, other.Health, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Reason, other.Reason, StringComparison.Ordinal)
            && CheckedAt == other.CheckedAt;

        public override int GetHashCode() => HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(UpstreamId),
            StringComparer.OrdinalIgnoreCase.GetHashCode(Health),
            Reason is null ? 0 : StringComparer.Ordinal.GetHashCode(Reason),
            CheckedAt);
    }

    private static string? SanitizeReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return null;
        var text = reason.Trim();
        return text.Length <= 128 ? text : text[..128];
    }
}
