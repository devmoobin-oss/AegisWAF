namespace AegisWAF.Gateway.Sites;

public sealed record SiteHealthTrend(
    int WindowSize,
    int Total,
    int Healthy,
    int Unhealthy,
    int Unknown,
    double HealthyPercent,
    int ConsecutiveUnhealthy,
    string AlertState,
    DateTimeOffset? LastCheckedAt,
    string RecoveryState = "none");

public static class SiteHealthTrendCalculator
{
    public const int MaxWindow = 64;
    public const int CriticalUnhealthyThreshold = 3;

    public static IReadOnlyList<SiteHealthObservation> FilterToCurrentUpstreams(
        IReadOnlyList<SiteHealthObservation> observations,
        IEnumerable<string> currentUpstreamIds)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(currentUpstreamIds);
        var current = currentUpstreamIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return observations
            .Where(x => current.Contains(x.UpstreamId))
            .ToArray();
    }

    public static SiteHealthTrend Calculate(IReadOnlyList<SiteHealthObservation> observations, int window, int expectedUpstreamCount = 0)
    {
        var boundedWindow = Math.Clamp(window, 1, MaxWindow);
        var selected = observations
            .OrderByDescending(x => x.CheckedAt)
            .Take(boundedWindow)
            .ToArray();
        if (selected.Length == 0)
            return new(boundedWindow, 0, 0, 0, 0, 0, 0, "no-data", null);

        var healthy = selected.Count(x => string.Equals(x.Health, "healthy", StringComparison.OrdinalIgnoreCase));
        var unhealthy = selected.Count(x => string.Equals(x.Health, "unhealthy", StringComparison.OrdinalIgnoreCase));
        var unknown = selected.Length - healthy - unhealthy;
        var expected = expectedUpstreamCount > 0
            ? expectedUpstreamCount
            : selected.Select(x => x.UpstreamId).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var batches = selected
            .GroupBy(x => NormalizeBatchTimestamp(x.CheckedAt))
            .OrderByDescending(x => x.Key)
            .Select(x => x.ToArray())
            .ToArray();
        var consecutiveUnhealthy = 0;
        foreach (var batch in batches)
        {
            if (!IsCompleteFailedBatch(batch, expected)) break;
            consecutiveUnhealthy++;
        }

        var latestBatchKey = NormalizeBatchTimestamp(batches[0][0].CheckedAt);
        var latestBatch = batches[0];
        var latestBatchComplete = IsCompleteBatch(latestBatch, expected);
        var latestBatchHealthy = latestBatchComplete && latestBatch.All(x => string.Equals(x.Health, "healthy", StringComparison.OrdinalIgnoreCase));
        var alertState = consecutiveUnhealthy >= CriticalUnhealthyThreshold
            ? "critical"
            : !latestBatchComplete || unhealthy > 0 || unknown > 0
                ? "degraded"
                : "healthy";
        var latestCheckedAt = selected[0].CheckedAt;
        var historicalFailureUpstreams = selected
            .Where(x => NormalizeBatchTimestamp(x.CheckedAt) < latestBatchKey &&
            (string.Equals(x.Health, "unhealthy", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(x.Health, "unknown", StringComparison.OrdinalIgnoreCase)))
            .Select(x => x.UpstreamId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var latestHealthyUpstreams = latestBatch
            .Where(x => string.Equals(x.Health, "healthy", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.UpstreamId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var historicalFailuresRecovered = historicalFailureUpstreams.Length > 0 &&
            historicalFailureUpstreams.All(latestHealthyUpstreams.Contains);
        var recoveryState = latestBatchHealthy && historicalFailuresRecovered ? "recovered" : "none";
        var healthyPercent = Math.Round(healthy * 100d / selected.Length, 2, MidpointRounding.AwayFromZero);
        return new(
            boundedWindow,
            selected.Length,
            healthy,
            unhealthy,
            unknown,
            healthyPercent,
            consecutiveUnhealthy,
            alertState,
            latestCheckedAt,
            recoveryState);
    }

    private static bool IsCompleteBatch(IReadOnlyList<SiteHealthObservation> batch, int expected) =>
        expected > 0 && batch.Select(x => x.UpstreamId).Distinct(StringComparer.OrdinalIgnoreCase).Count() >= expected;

    private static bool IsCompleteFailedBatch(IReadOnlyList<SiteHealthObservation> batch, int expected) =>
        IsCompleteBatch(batch, expected) && batch.All(x => string.Equals(x.Health, "unhealthy", StringComparison.OrdinalIgnoreCase));

    private static DateTimeOffset NormalizeBatchTimestamp(DateTimeOffset value)
    {
        var utc = value.UtcDateTime;
        var ticks = utc.Ticks - utc.Ticks % TimeSpan.TicksPerSecond;
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }
}
