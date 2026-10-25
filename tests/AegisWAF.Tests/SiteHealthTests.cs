using AegisWAF.Gateway.Sites;

namespace AegisWAF.Tests;

public sealed class SiteHealthTests
{
    [Fact]
    public void Selects_the_only_healthy_upstream()
    {
        var site = Site(new SiteUpstream("primary", new Uri("https://primary.example.com/")));
        var health = new SiteHealthRegistry();
        health.MarkHealthy(site.Id, "primary");

        var selected = health.Select(site);

        Assert.Equal("primary", selected?.Id);
    }

    [Fact]
    public void Uses_weighted_round_robin_for_multiple_healthy_upstreams()
    {
        var site = Site(
            new SiteUpstream("primary", new Uri("https://primary.example.com/"), 1),
            new SiteUpstream("secondary", new Uri("https://secondary.example.com/"), 3));
        var health = new SiteHealthRegistry();
        health.MarkHealthy(site.Id, "primary");
        health.MarkHealthy(site.Id, "secondary");

        var selected = Enumerable.Range(0, 4).Select(_ => health.Select(site)!.Id).ToArray();

        Assert.Equal(["primary", "secondary", "secondary", "secondary"], selected);
    }

    [Fact]
    public void Returns_no_upstream_when_all_are_unhealthy()
    {
        var site = Site(new SiteUpstream("primary", new Uri("https://primary.example.com/")));
        var health = new SiteHealthRegistry();
        health.MarkUnhealthy(site.Id, "primary", "timeout");

        Assert.Null(health.Select(site));
        Assert.Equal("timeout", health.Get(site.Id, "primary")?.Reason);
    }

    private static GatewaySite Site(params SiteUpstream[] upstreams) =>
        new("site-a", true, ["app-a.example.com"], ["/"], upstreams, "default",
            new SiteRateLimitPolicy(), new SiteEgressPolicy(upstreams.Select(x => x.Address.Host).ToArray(), new[] { 443 }, new[] { "https" }));
}
