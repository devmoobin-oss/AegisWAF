using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using AegisWAF.Gateway.Traffic;

namespace AegisWAF.Tests;

public sealed class TrafficTelemetryModelTests
{
    [Fact]
    public void Sanitizes_query_string_and_bounds_the_path()
    {
        var path = TrafficTelemetry.SanitizePath(new PathString("/search"), new QueryString("?token=secret&q=union+select"));

        Assert.Equal("/search", path);
    }

    [Fact]
    public void Traffic_record_contains_operational_fields_but_no_sensitive_payload_fields()
    {
        var names = typeof(TrafficRecord).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(x => x.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains(nameof(TrafficRecord.TraceId), names);
        Assert.Contains(nameof(TrafficRecord.ClientIp), names);
        Assert.Contains(nameof(TrafficRecord.SiteId), names);
        Assert.Contains(nameof(TrafficRecord.UpstreamAddress), names);
        Assert.Contains(nameof(TrafficRecord.Decision), names);
        Assert.Contains(nameof(TrafficRecord.OwaspCodes), names);
        Assert.DoesNotContain("Body", names);
        Assert.DoesNotContain("ResponseBody", names);
        Assert.DoesNotContain("Cookie", names);
        Assert.DoesNotContain("Authorization", names);
        Assert.DoesNotContain("RawQuery", names);
    }

    [Fact]
    public void Query_defaults_to_safe_limit_and_supports_optional_filters()
    {
        var query = new TrafficQuery();

        Assert.Equal(100, query.Limit);
        Assert.Null(query.Decision);
        Assert.Null(query.SiteId);
        Assert.Null(query.ClientIp);
        Assert.Null(query.Search);
    }

    [Fact]
    public void Unmatched_request_keeps_listener_hostname_for_diagnostics()
    {
        var host = new HostString("127.0.0.1", 5080);

        Assert.Equal("127.0.0.1", TrafficTelemetry.ResolveHostname(null, host));
        Assert.Equal("site.example", TrafficTelemetry.ResolveHostname("site.example", host));
    }
}

public sealed class TrafficTelemetryStoreTests
{
    [Fact]
    public void Ring_buffer_evicts_oldest_record_and_reports_drop()
    {
        var telemetry = Create(capacity: 2);
        telemetry.Record(Record("/one"));
        telemetry.Record(Record("/two"));
        telemetry.Record(Record("/three"));

        var result = telemetry.Query(new TrafficQuery(500));

        Assert.Equal(2, result.Count);
        Assert.DoesNotContain(result, x => x.Path == "/one");
        Assert.Equal(1, telemetry.GetSummary().DroppedEvents);
    }

    [Fact]
    public void Query_caps_limit_and_filters_without_exposing_mutable_storage()
    {
        var telemetry = Create(capacity: 5);
        telemetry.Record(Record("/allowed", TrafficDecision.Allowed, siteId: "site-a", clientIp: "10.0.0.1"));
        telemetry.Record(Record("/blocked", TrafficDecision.Blocked, siteId: "site-b", clientIp: "10.0.0.2"));

        var result = telemetry.Query(new TrafficQuery(500, TrafficDecision.Blocked, "site-b", "10.0.0.2"));

        Assert.Single(result);
        Assert.Equal("/blocked", result[0].Path);
        Assert.True(telemetry.Query(new TrafficQuery(1)).Count <= 1);
    }

    [Fact]
    public void Active_connection_scope_is_idempotent_and_never_goes_negative()
    {
        var telemetry = Create(capacity: 2);
        var scope = telemetry.BeginRequest();

        Assert.Equal(1, telemetry.GetSummary().ActiveConnections);
        scope.Dispose();
        scope.Dispose();

        Assert.Equal(0, telemetry.GetSummary().ActiveConnections);
    }

    private static TrafficTelemetry Create(int capacity) => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Waf:Telemetry:LiveCapacity"] = capacity.ToString() })
        .Build());

    private static TrafficRecord Record(string path, TrafficDecision decision = TrafficDecision.Allowed, string siteId = "site-a", string clientIp = "127.0.0.1") =>
        new(DateTimeOffset.UtcNow, Guid.NewGuid().ToString("N"), clientIp, siteId, "example.test", "GET", path,
            "primary", "http://127.0.0.1:8080/", decision, decision == TrafficDecision.Blocked ? 403 : 200,
            decision == TrafficDecision.Blocked ? "sqli-01" : null, decision == TrafficDecision.Blocked ? ["A03"] : [], 5);
}
