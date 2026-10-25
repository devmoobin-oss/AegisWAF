using System.Collections.Concurrent;

namespace AegisWAF.Gateway.Sites;

public sealed record SiteHealthState(bool Healthy, DateTimeOffset LastChecked, string? Reason);

public sealed class SiteHealthRegistry
{
    private readonly ConcurrentDictionary<string, SiteHealthState> _states = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _cursors = new(StringComparer.Ordinal);

    public void MarkHealthy(string siteId, string upstreamId) =>
        _states[Key(siteId, upstreamId)] = new(true, DateTimeOffset.UtcNow, null);

    public void MarkUnhealthy(string siteId, string upstreamId, string reason) =>
        _states[Key(siteId, upstreamId)] = new(false, DateTimeOffset.UtcNow, reason.Length > 128 ? reason[..128] : reason);

    public SiteHealthState? Get(string siteId, string upstreamId) =>
        _states.TryGetValue(Key(siteId, upstreamId), out var state) ? state : null;

    public SiteUpstream? Select(GatewaySite site)
    {
        var healthy = site.Upstreams.Where(upstream => Get(site.Id, upstream.Id)?.Healthy != false).ToArray();
        if (healthy.Length == 0) return null;

        var totalWeight = healthy.Sum(x => Math.Clamp(x.Weight, 1, 1_000));
        var cursor = _cursors.AddOrUpdate(site.Id, 0, (_, value) => (value + 1) % totalWeight);
        var offset = cursor % totalWeight;
        foreach (var upstream in healthy)
        {
            var weight = Math.Clamp(upstream.Weight, 1, 1_000);
            if (offset < weight) return upstream;
            offset -= weight;
        }
        return healthy[^1];
    }

    private static string Key(string siteId, string upstreamId) => siteId + "\0" + upstreamId;
}
