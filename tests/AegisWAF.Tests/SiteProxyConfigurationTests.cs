using AegisWAF.Gateway.Sites;

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
}
