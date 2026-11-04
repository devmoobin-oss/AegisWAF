using System.Text;
using System.Text.Json;
using AegisWAF.Gateway;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AegisWAF.Tests;

public sealed class HealthResponseWriterTests
{
    [Fact]
    public async Task WriteAsync_EmitsSafePerCheckStatusWithoutSensitiveDetails()
    {
        var context = new DefaultHttpContext();
        await using var body = new MemoryStream();
        context.Response.Body = body;
        var report = new HealthReport(
            new Dictionary<string, HealthReportEntry>
            {
                ["site-runtime-listener"] = new(
                    HealthStatus.Degraded,
                    "Site runtime listener is degraded",
                    TimeSpan.FromMilliseconds(2),
                    new InvalidOperationException("Host=secret-db;Password=do-not-leak"),
                    null),
                ["postgres"] = new(
                    HealthStatus.Healthy,
                    "PostgreSQL is available",
                    TimeSpan.FromMilliseconds(1),
                    null,
                    null)
            },
            TimeSpan.FromMilliseconds(3));

        await HealthResponseWriter.WriteAsync(context, report);

        var json = Encoding.UTF8.GetString(body.ToArray());
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("Degraded", root.GetProperty("status").GetString());
        Assert.Equal("Degraded", root.GetProperty("entries").GetProperty("site-runtime-listener").GetProperty("status").GetString());
        Assert.Equal("Site runtime listener is degraded", root.GetProperty("entries").GetProperty("site-runtime-listener").GetProperty("description").GetString());
        Assert.DoesNotContain("secret-db", json, StringComparison.Ordinal);
        Assert.DoesNotContain("do-not-leak", json, StringComparison.Ordinal);
        Assert.Equal("application/json; charset=utf-8", context.Response.ContentType);
    }
}
