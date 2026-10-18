using AegisWAF.Gateway;

namespace AegisWAF.Tests;

public sealed class SecurityEventLoggingTests
{
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
