namespace AegisWAF.Gateway.Sites;

/// <summary>Projects YARP's active/passive destination states into safe API/readiness semantics.</summary>
public static class SiteProxyHealth
{
    public static string Status(string? active, string? passive)
    {
        if (string.Equals(active, "Unhealthy", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(passive, "Unhealthy", StringComparison.OrdinalIgnoreCase)) return "unhealthy";
        if (string.Equals(active, "Healthy", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(passive, "Healthy", StringComparison.OrdinalIgnoreCase)) return "healthy";
        return "unknown";
    }

    public static bool IsAvailable(string? active, string? passive, bool requireKnownHealthy = false)
    {
        if (string.Equals(active, "Unhealthy", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(passive, "Unhealthy", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!requireKnownHealthy) return true;
        return string.Equals(active, "Healthy", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(passive, "Healthy", StringComparison.OrdinalIgnoreCase);
    }
}
