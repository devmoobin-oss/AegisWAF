namespace AegisWAF.Gateway.Sites;

public sealed record SiteHealthState(bool Healthy, DateTimeOffset LastChecked, string? Reason);

/// <summary>Bounded process-local health state used by the legacy/manual selection path.</summary>
public sealed class SiteHealthRegistry
{
    public const int DefaultMaxSites = 1_024;

    private readonly object _gate = new();
    private readonly int _maxSites;
    private readonly Dictionary<string, SiteBucket> _sites = new(StringComparer.OrdinalIgnoreCase);
    private long _touchSequence;

    public SiteHealthRegistry(int maxSites = DefaultMaxSites)
    {
        if (maxSites is < 1 or > 100_000) throw new ArgumentOutOfRangeException(nameof(maxSites));
        _maxSites = maxSites;
    }

    public void MarkHealthy(string siteId, string upstreamId)
    {
        lock (_gate)
        {
            var bucket = GetOrCreateSite(siteId);
            bucket.LastTouched = NextTouch();
            bucket.States[upstreamId ?? string.Empty] = new(true, DateTimeOffset.UtcNow, null);
        }
    }

    public void MarkUnhealthy(string siteId, string upstreamId, string reason)
    {
        lock (_gate)
        {
            var bucket = GetOrCreateSite(siteId);
            bucket.LastTouched = NextTouch();
            bucket.States[upstreamId ?? string.Empty] = new(false, DateTimeOffset.UtcNow, reason.Length > 128 ? reason[..128] : reason);
        }
    }

    public SiteHealthState? Get(string siteId, string upstreamId)
    {
        lock (_gate)
        {
            if (!_sites.TryGetValue(siteId ?? string.Empty, out var bucket) ||
                !bucket.States.TryGetValue(upstreamId ?? string.Empty, out var state)) return null;
            bucket.LastTouched = NextTouch();
            return state;
        }
    }

    public SiteUpstream? Select(GatewaySite site)
    {
        lock (_gate)
        {
            var bucket = GetOrCreateSite(site.Id);
            bucket.LastTouched = NextTouch();
            var healthy = site.Upstreams
                .Where(upstream => !bucket.States.TryGetValue(upstream.Id, out var state) || state.Healthy)
                .ToArray();
            if (healthy.Length == 0) return null;

            var totalWeight = healthy.Sum(x => Math.Clamp(x.Weight, 1, 1_000));
            var offset = bucket.Cursor % totalWeight;
            bucket.Cursor = (offset + 1) % totalWeight;
            foreach (var upstream in healthy)
            {
                var weight = Math.Clamp(upstream.Weight, 1, 1_000);
                if (offset < weight) return upstream;
                offset -= weight;
            }
            return healthy[^1];
        }
    }

    private SiteBucket GetOrCreateSite(string siteId)
    {
        var key = siteId ?? string.Empty;
        if (_sites.TryGetValue(key, out var existing)) return existing;

        if (_sites.Count >= _maxSites)
        {
            var leastRecentlyUsed = _sites.MinBy(x => x.Value.LastTouched).Key;
            _sites.Remove(leastRecentlyUsed);
        }

        var bucket = new SiteBucket();
        _sites[key] = bucket;
        return bucket;
    }

    private long NextTouch() => _touchSequence == long.MaxValue ? long.MaxValue : ++_touchSequence;

    private sealed class SiteBucket
    {
        public Dictionary<string, SiteHealthState> States { get; } = new(StringComparer.Ordinal);
        public long LastTouched { get; set; }
        public int Cursor { get; set; }
    }
}
