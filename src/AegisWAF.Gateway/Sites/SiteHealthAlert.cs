namespace AegisWAF.Gateway.Sites;

public sealed record SiteHealthAlertAcknowledgement(
    string SiteId,
    string AlertState,
    DateTimeOffset AcknowledgedAt,
    DateTimeOffset AcknowledgedUntil,
    string AcknowledgedBy);

public sealed record SiteHealthAlertAcknowledgeRequest(int CooldownSeconds = 3_600);

public sealed record SiteHealthComparisonItem(string SiteId, SiteHealthTrend Trend, bool IsStale);

public static class HealthAlertLifecycle
{
    public const int MinCooldownSeconds = 60;
    public const int MaxCooldownSeconds = 86_400;
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(15);

    public static bool TryValidateCooldown(int seconds, out string? error)
    {
        if (seconds is < MinCooldownSeconds or > MaxCooldownSeconds)
        {
            error = $"cooldownSeconds must be between {MinCooldownSeconds} and {MaxCooldownSeconds}";
            return false;
        }
        error = null;
        return true;
    }

    public static bool IsActionable(string? alertState) =>
        string.Equals(alertState, "critical", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(alertState, "degraded", StringComparison.OrdinalIgnoreCase);

    public static bool IsActionable(SiteHealthTrend trend) =>
        IsActionable(trend.AlertState) &&
        !string.Equals(trend.RecoveryState, "recovered", StringComparison.OrdinalIgnoreCase);

    public static bool IsStale(SiteHealthTrend trend, DateTimeOffset now) =>
        trend.LastCheckedAt is null || now - trend.LastCheckedAt.Value > StaleAfter;

    public static bool IsActionable(SiteHealthTrend trend, DateTimeOffset now) =>
        IsActionable(trend) && !IsStale(trend, now);

    public static bool IsActionable(SiteHealthComparisonItem item) =>
        !item.IsStale && IsActionable(item.Trend);

    public static string EffectiveAlertState(SiteHealthComparisonItem item) =>
        EffectiveAlertState(item.Trend, item.IsStale);

    public static string EffectiveAlertState(SiteHealthTrend trend, bool isStale)
    {
        ArgumentNullException.ThrowIfNull(trend);
        if (isStale) return "stale";
        if (string.Equals(trend.RecoveryState, "recovered", StringComparison.OrdinalIgnoreCase)) return "recovered";
        return string.IsNullOrWhiteSpace(trend.AlertState) ? "unknown" : trend.AlertState.Trim().ToLowerInvariant();
    }

    public static bool IsSuppressed(string currentState, string acknowledgedState, DateTimeOffset? acknowledgedUntil, DateTimeOffset now) =>
        IsActionable(currentState) &&
        string.Equals(currentState, acknowledgedState, StringComparison.OrdinalIgnoreCase) &&
        acknowledgedUntil is DateTimeOffset until && until > now;
}

public static class SiteHealthComparisonCalculator
{
    public static IReadOnlyList<SiteHealthComparisonItem> Calculate(
        IEnumerable<(string SiteId, IReadOnlyList<SiteHealthObservation> Observations)> sites,
        int window,
        int limit,
        DateTimeOffset now,
        IReadOnlyDictionary<string, int>? expectedUpstreamCounts = null)
    {
        var boundedLimit = Math.Clamp(limit, 1, 100);
        return sites
            .Where(x => !string.IsNullOrWhiteSpace(x.SiteId))
            .Select(x =>
            {
                var expected = expectedUpstreamCounts is not null && expectedUpstreamCounts.TryGetValue(x.SiteId, out var count)
                    ? count
                    : 0;
                var trend = SiteHealthTrendCalculator.Calculate(x.Observations, window, expected);
                var stale = HealthAlertLifecycle.IsStale(trend, now);
                return new SiteHealthComparisonItem(x.SiteId, trend, stale);
            })
            .OrderBy(x => AlertRank(x.Trend))
            .ThenBy(x => IsStaleActionable(x) ? x.IsStale : false)
            .ThenByDescending(x => x.IsStale)
            .ThenBy(x => x.SiteId, StringComparer.OrdinalIgnoreCase)
            .Take(boundedLimit)
            .ToArray();
    }

    private static int AlertRank(SiteHealthTrend trend)
    {
        if (string.Equals(trend.RecoveryState, "recovered", StringComparison.OrdinalIgnoreCase)) return 2;
        return trend.AlertState switch
        {
            "critical" => 0,
            "degraded" => 1,
            "no-data" => 3,
            _ => 2
        };
    }

    private static bool IsStaleActionable(SiteHealthComparisonItem item) =>
        item.IsStale && HealthAlertLifecycle.IsActionable(item.Trend);
}
