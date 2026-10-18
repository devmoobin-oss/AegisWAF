using AegisWAF.Gateway;
using AegisWAF.Gateway.Sites;
using Microsoft.Extensions.Configuration;

namespace AegisWAF.Tests;

public sealed class SiteScopedSecurityTests
{
    [Fact]
    public async Task Memory_rate_limit_isolated_by_site_and_route()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Waf:RateLimit:Requests"] = "2",
            ["Waf:RateLimit:GlobalRequests"] = "0",
            ["Waf:RateLimit:WindowSeconds"] = "60",
            ["Waf:RateLimit:MaxEntries"] = "100"
        }).Build();
        using var limiter = new SlidingRateLimiter(config);

        Assert.True(await limiter.AllowAsync("site-a", "198.51.100.7", "/login", CancellationToken.None));
        Assert.True(await limiter.AllowAsync("site-a", "198.51.100.7", "/login", CancellationToken.None));
        Assert.False(await limiter.AllowAsync("site-a", "198.51.100.7", "/login", CancellationToken.None));
        Assert.True(await limiter.AllowAsync("site-b", "198.51.100.7", "/login", CancellationToken.None));
        Assert.True(await limiter.AllowAsync("site-a", "198.51.100.7", "/health", CancellationToken.None));
    }

    [Fact]
    public void Site_egress_policy_only_allows_the_site_host_and_port()
    {
        var site = new GatewaySite("site-a", true, ["app-a.example.com"], ["/"],
            [new SiteUpstream("primary", new Uri("https://origin-a.example.com/"))], "default",
            new SiteRateLimitPolicy(), new SiteEgressPolicy(["origin-a.example.com"], [443], ["https"]));

        Assert.True(SiteEgressPolicyEvaluator.IsExplicitlyAllowed(site, new Uri("https://origin-a.example.com/api")));
        Assert.False(SiteEgressPolicyEvaluator.IsExplicitlyAllowed(site, new Uri("https://origin-b.example.com/api")));
        Assert.False(SiteEgressPolicyEvaluator.IsExplicitlyAllowed(site, new Uri("http://origin-a.example.com/api")));
    }

    [Fact]
    public void Security_events_include_site_context_without_upstream_secrets()
    {
        var item = new SecurityEvent(DateTimeOffset.UnixEpoch, "198.51.100.7", "GET", "/x", "SQL Injection", 403, "req-123",
            "site-a", "app-a.example.com", "primary");

        var json = SecurityEventLogFormatter.Format(item);

        Assert.Contains("site-a", json, StringComparison.Ordinal);
        Assert.Contains("app-a.example.com", json, StringComparison.Ordinal);
        Assert.Contains("primary", json, StringComparison.Ordinal);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
    }
}
