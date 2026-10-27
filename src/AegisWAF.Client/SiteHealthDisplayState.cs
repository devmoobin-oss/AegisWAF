namespace AegisWAF.Client;

/// <summary>
/// Resolves the operator-facing Site alert state from the current runtime health and historical trend.
/// Historical alerts must not keep a recovered upstream visually critical.
/// </summary>
public static class SiteHealthDisplayState
{
    public static string Resolve(string? liveHealth, string? historicalAlert, string? recoveryState, bool pendingRestart)
    {
        if (pendingRestart) return "pending-restart";

        var live = Normalize(liveHealth);
        var historical = Normalize(historicalAlert);
        var recovered = string.Equals(recoveryState, "recovered", StringComparison.OrdinalIgnoreCase);

        if (live == "healthy")
            return historical is "critical" or "degraded" || recovered ? "recovered" : "healthy";

        if (live == "unhealthy")
            return historical is "critical" or "degraded" ? historical : "unhealthy";
        if (live == "invalid") return "invalid";
        if (live == "unknown")
            return historical is "critical" or "degraded" or "healthy" or "no-data" ? historical : "unknown";
        return "unknown";
    }

    private static string Normalize(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();
}
