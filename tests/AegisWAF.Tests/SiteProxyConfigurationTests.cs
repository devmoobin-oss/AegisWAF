using AegisWAF.Gateway.Sites;
using Microsoft.Extensions.Configuration;

namespace AegisWAF.Tests;

public sealed class SiteProxyConfigurationTests
{
    [Fact]
    public void Builds_host_and_cluster_entries_for_each_site_upstream()
    {
        var snapshot = new SiteConfigurationSnapshot([
            new GatewaySite("site-a", true, ["app-a.example.com"], ["/"],
                [new SiteUpstream("primary", new Uri("https://origin-a.example.com/"))], "strict",
                new SiteRateLimitPolicy(), new SiteEgressPolicy(["origin-a.example.com"], [443], ["https"]))
        ], false);

        var values = SiteProxyConfiguration.Build(snapshot);

        Assert.Equal("site-site-a", values["ReverseProxy:Routes:site-site-a:ClusterId"]);
        Assert.Equal("app-a.example.com", values["ReverseProxy:Routes:site-site-a:Match:Hosts:0"]);
        Assert.Equal("https://origin-a.example.com/", values["ReverseProxy:Clusters:site-site-a:Destinations:primary:Address"]);
    }

    [Fact]
    public void Registry_resolves_site_context_and_rejects_unknown_host()
    {
        var site = new GatewaySite("site-a", true, ["app-a.example.com"], ["/"],
            [new SiteUpstream("primary", new Uri("https://origin-a.example.com/"))], "strict",
            new SiteRateLimitPolicy(), new SiteEgressPolicy(["origin-a.example.com"], [443], ["https"]));
        var registry = new SiteRegistry(new SiteConfigurationSnapshot([site], false));

        var match = registry.Resolve("app-a.example.com:443", "/");
        Assert.NotNull(match);
        Assert.Equal("site-a", match!.Site.Id);
        Assert.Null(registry.Resolve("unknown.example.com", "/"));
    }

    [Fact]
    public void Disables_all_legacy_routes_when_explicit_sites_are_configured()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ReverseProxy:Routes:route1:Match:Path"] = "{**catch-all}",
            ["ReverseProxy:Routes:custom:Match:Path"] = "/legacy/{**remainder}"
        }).Build();
        var snapshot = new SiteConfigurationSnapshot([], false);

        var values = SiteProxyConfiguration.Build(snapshot, config);

        Assert.Equal("__aegiswaf_legacy_route_disabled__", values["ReverseProxy:Routes:route1:Match:Hosts:0"]);
        Assert.Equal("__aegiswaf_legacy_route_disabled__", values["ReverseProxy:Routes:custom:Match:Hosts:0"]);
    }

    [Fact]
    public void Creates_distinct_routes_for_multiple_hostnames_on_one_site()
    {
        var site = new GatewaySite("site-a", true, ["app-a.example.com", "app-b.example.com"], ["/"],
            [new SiteUpstream("primary", new Uri("https://origin.example.com/"))], "default",
            new SiteRateLimitPolicy(), new SiteEgressPolicy(["origin.example.com"], [443], ["https"]));

        var values = SiteProxyConfiguration.Build(new SiteConfigurationSnapshot([site], false));
        var routeHosts = values.Where(x => x.Key.Contains("ReverseProxy:Routes:", StringComparison.Ordinal) && x.Key.EndsWith(":Match:Hosts:0", StringComparison.Ordinal)).Select(x => x.Value).ToArray();

        Assert.Contains("app-a.example.com", routeHosts);
        Assert.Contains("app-b.example.com", routeHosts);
    }

    [Fact]
    public void Configures_active_and_passive_health_checks_for_each_generated_cluster()
    {
        var site = new GatewaySite("site-a", true, ["app-a.example.com"], ["/"],
            [new SiteUpstream("primary", new Uri("https://origin.example.com/"))], "default",
            new SiteRateLimitPolicy(), new SiteEgressPolicy(["origin.example.com"], [443], ["https"]));

        var values = SiteProxyConfiguration.Build(new SiteConfigurationSnapshot([site], false));

        Assert.Equal("true", values["ReverseProxy:Clusters:site-site-a:HealthCheck:Active:Enabled"]);
        Assert.Equal("/health", values["ReverseProxy:Clusters:site-site-a:HealthCheck:Active:Path"]);
        Assert.Equal("HealthyAndUnknown", values["ReverseProxy:Clusters:site-site-a:HealthCheck:AvailableDestinationsPolicy"]);
        Assert.Equal("true", values["ReverseProxy:Clusters:site-site-a:HealthCheck:Passive:Enabled"]);
        Assert.Equal("TransportFailureRate", values["ReverseProxy:Clusters:site-site-a:HealthCheck:Passive:Policy"]);
    }

    [Fact]
    public void Uses_bounded_per_site_health_policy_in_generated_cluster()
    {
        var site = new GatewaySite("site-a", true, ["app-a.example.com"], ["/"],
            [new SiteUpstream("primary", new Uri("https://origin.example.com/"))], "default",
            new SiteRateLimitPolicy(), new SiteEgressPolicy(["origin.example.com"], [443], ["https"]),
            new SiteHealthPolicy("/live", 15, 4, 45));

        var values = SiteProxyConfiguration.Build(new SiteConfigurationSnapshot([site], false));

        Assert.Equal("/live", values["ReverseProxy:Clusters:site-site-a:HealthCheck:Active:Path"]);
        Assert.Equal("00:00:15", values["ReverseProxy:Clusters:site-site-a:HealthCheck:Active:Interval"]);
        Assert.Equal("00:00:04", values["ReverseProxy:Clusters:site-site-a:HealthCheck:Active:Timeout"]);
        Assert.Equal("00:00:45", values["ReverseProxy:Clusters:site-site-a:HealthCheck:Passive:ReactivationPeriod"]);
    }

    [Fact]
    public void Disables_every_host_entry_on_legacy_routes()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ReverseProxy:Routes:legacy:Match:Hosts:0"] = "old.example.com",
            ["ReverseProxy:Routes:legacy:Match:Hosts:1"] = "other.example.com"
        }).Build();

        var values = SiteProxyConfiguration.Build(new SiteConfigurationSnapshot([], false), config);

        Assert.Equal("__aegiswaf_legacy_route_disabled__", values["ReverseProxy:Routes:legacy:Match:Hosts:0"]);
        Assert.Equal("__aegiswaf_legacy_route_disabled__", values["ReverseProxy:Routes:legacy:Match:Hosts:1"]);
    }

    [Fact]
    public void Generated_site_pipeline_enables_load_balancing()
    {
        var site = new GatewaySite("site-a", true, ["app-a.example.com"], ["/"],
            [new SiteUpstream("primary", new Uri("https://origin.example.com/"))], "default",
            new SiteRateLimitPolicy(), new SiteEgressPolicy(["origin.example.com"], [443], ["https"]));

        var values = SiteProxyConfiguration.Build(new SiteConfigurationSnapshot([site], false));

        Assert.Equal("PowerOfTwoChoices", values["ReverseProxy:Clusters:site-site-a:LoadBalancingPolicy"]);
    }
}
