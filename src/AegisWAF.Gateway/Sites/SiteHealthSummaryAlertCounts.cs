namespace AegisWAF.Gateway.Sites;

public sealed record SiteHealthSummaryAlertCountResult(
    int CriticalSites,
    int DegradedSites,
    int HealthySites,
    int NoDataSites,
    int StaleSites,
    int RecoveredSites,
    int StaleCriticalSites,
    int StaleDegradedSites);

public static class SiteHealthSummaryAlertCounts
{
    public static SiteHealthSummaryAlertCountResult Calculate(IEnumerable<SiteHealthComparisonItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var materialized = items.ToArray();
        return new(
            CriticalSites: materialized.Count(x => !x.IsStale && Is(x.Trend.AlertState, "critical")),
            DegradedSites: materialized.Count(x => !x.IsStale && Is(x.Trend.AlertState, "degraded") && !Is(x.Trend.RecoveryState, "recovered")),
            HealthySites: materialized.Count(x => Is(x.Trend.AlertState, "healthy")),
            NoDataSites: materialized.Count(x => Is(x.Trend.AlertState, "no-data")),
            StaleSites: materialized.Count(x => x.IsStale),
            RecoveredSites: materialized.Count(x => Is(x.Trend.RecoveryState, "recovered")),
            StaleCriticalSites: materialized.Count(x => x.IsStale && Is(x.Trend.AlertState, "critical")),
            StaleDegradedSites: materialized.Count(x => x.IsStale && Is(x.Trend.AlertState, "degraded")));
    }

    private static bool Is(string? value, string expected) =>
        string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
}
