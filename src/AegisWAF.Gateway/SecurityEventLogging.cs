using System.Text.Json;
using AegisWAF.Client;

namespace AegisWAF.Gateway;

internal static class SecurityEventLogFormatter
{
    public static string Format(SecurityEvent item) => JsonSerializer.Serialize(new
    {
        eventType = "aegiswaf.security_event",
        timestamp = item.Timestamp,
        clientIp = item.Ip,
        method = item.Method,
        path = item.Path,
        requestId = item.RequestId,
        rule = item.Rule,
        status = item.Status,
        owaspCodes = CodesForEvent(item.Rule)
    });

    private static IReadOnlyList<string> CodesForEvent(string rule)
    {
        var mapped = OwaspCatalog.ForRule(rule).Codes;
        if (mapped.Count > 0) return mapped;
        if (rule.Contains("Injection", StringComparison.OrdinalIgnoreCase) ||
            rule.Contains("XSS", StringComparison.OrdinalIgnoreCase) ||
            rule.Contains("Traversal", StringComparison.OrdinalIgnoreCase) ||
            rule.Contains("XXE", StringComparison.OrdinalIgnoreCase) ||
            rule.Contains("Template", StringComparison.OrdinalIgnoreCase) ||
            rule.Contains("Anomaly", StringComparison.OrdinalIgnoreCase)) return ["A03"];
        return [];
    }
}
