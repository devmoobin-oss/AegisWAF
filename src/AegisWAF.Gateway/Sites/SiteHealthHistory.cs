namespace AegisWAF.Gateway.Sites;

public sealed record SiteHealthObservation(
    string UpstreamId,
    string Health,
    string? Reason,
    DateTimeOffset CheckedAt);

/// <summary>Bounded process-local health history. It deliberately omits upstream addresses and credentials.</summary>
public sealed class SiteHealthHistory
{
    private readonly object _gate = new();
    private readonly int _maxObservationsPerSite;
    private readonly Dictionary<string, Queue<SiteHealthObservation>> _observations = new(StringComparer.OrdinalIgnoreCase);

    public SiteHealthHistory(int maxObservationsPerSite = 64)
    {
        if (maxObservationsPerSite is < 1 or > 256) throw new ArgumentOutOfRangeException(nameof(maxObservationsPerSite));
        _maxObservationsPerSite = maxObservationsPerSite;
    }

    public void Record(string siteId, IReadOnlyList<SiteHealthProbeResult> results)
    {
        if (string.IsNullOrWhiteSpace(siteId) || results.Count == 0) return;
        lock (_gate)
        {
            if (!_observations.TryGetValue(siteId, out var queue))
                _observations[siteId] = queue = new Queue<SiteHealthObservation>(_maxObservationsPerSite);
            foreach (var result in results)
            {
                queue.Enqueue(new SiteHealthObservation(result.UpstreamId, result.Health, SanitizeReason(result.Reason), result.CheckedAt));
                while (queue.Count > _maxObservationsPerSite) queue.Dequeue();
            }
        }
    }

    public IReadOnlyList<SiteHealthObservation> Read(string siteId, int limit = 20)
    {
        limit = Math.Clamp(limit, 1, 100);
        lock (_gate)
        {
            if (!_observations.TryGetValue(siteId, out var queue)) return [];
            return queue.Reverse().Take(limit).ToArray();
        }
    }

    private static string? SanitizeReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return null;
        var text = reason.Trim();
        return text.Length <= 128 ? text : text[..128];
    }
}
