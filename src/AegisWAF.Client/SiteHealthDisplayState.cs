namespace AegisWAF.Client;

/// <summary>
/// Resolves the operator-facing Site alert state from the current runtime health and historical trend.
/// Historical alerts must not keep a recovered upstream visually critical.
/// </summary>
public static class SiteHealthDisplayState
{
    public static string Resolve(string? liveHealth, string? historicalAlert, string? recoveryState, bool pendingRestart, bool stale = false, string? effectiveAlertState = null)
    {
        if (pendingRestart) return "pending-restart";

        var live = Normalize(liveHealth);
        var historical = Normalize(string.IsNullOrWhiteSpace(effectiveAlertState) ? historicalAlert : effectiveAlertState);
        var recovered = string.Equals(recoveryState, "recovered", StringComparison.OrdinalIgnoreCase);

        // A historical alert with no fresh runtime data is not evidence of a current outage.
        // Keep the operator informed without presenting stale state as an active crisis.
        if (stale && live is not "healthy" and not "unhealthy") return "stale";

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
