using System.Text.Json;
using AegisWAF.Client;

namespace AegisWAF.Gateway;

internal static class SecurityEventLogFormatter
{
    public static string FormatAuditIntegrityFailure(long checkedEntries, long firstInvalidId) => JsonSerializer.Serialize(new
    {
        eventType = "aegiswaf.audit_integrity_failure",
        timestamp = DateTimeOffset.UtcNow,
        severity = "critical",
        checkedEntries,
        firstInvalidId
    });

    public static string Format(SecurityEvent item) => JsonSerializer.Serialize(new
    {
        eventType = "aegiswaf.security_event",
        timestamp = item.Timestamp,
        clientIp = item.Ip,
        method = item.Method,
        path = item.Path,
        siteId = item.SiteId,
        hostname = item.Hostname,
        upstreamId = item.UpstreamId,
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
