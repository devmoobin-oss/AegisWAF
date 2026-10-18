using AegisWAF.Gateway;
using System.Text.Json;

namespace AegisWAF.Tests;

public sealed class SecurityEventLoggingTests
{
    [Fact]
    public void Audit_integrity_failure_is_emitted_as_a_structured_critical_event()
    {
        var json = SecurityEventLogFormatter.FormatAuditIntegrityFailure(42, 9001);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("aegiswaf.audit_integrity_failure", root.GetProperty("eventType").GetString());
        Assert.Equal("critical", root.GetProperty("severity").GetString());
        Assert.Equal(42, root.GetProperty("checkedEntries").GetInt64());
        Assert.Equal(9001, root.GetProperty("firstInvalidId").GetInt64());
    }

    [Fact]
    public void Formats_a_structured_event_with_owasp_mapping()
    {
        var item = new SecurityEvent(DateTimeOffset.UnixEpoch, "198.51.100.7", "GET", "/x", "SQL Injection", 403, "req-123");

        var json = SecurityEventLogFormatter.Format(item);

        Assert.Contains("aegiswaf.security_event", json, StringComparison.Ordinal);
        Assert.Contains("198.51.100.7", json, StringComparison.Ordinal);
        Assert.Contains("SQL Injection", json, StringComparison.Ordinal);
        Assert.Contains("A03", json, StringComparison.Ordinal);
        Assert.Contains("req-123", json, StringComparison.Ordinal);
    }
}
