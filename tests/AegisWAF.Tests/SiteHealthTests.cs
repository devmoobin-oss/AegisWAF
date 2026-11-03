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
    public void Honors_the_configured_weight_above_the_legacy_thousand_limit()
    {
        var site = Site(
            new SiteUpstream("primary", new Uri("https://primary.example.com/"), 10_000),
            new SiteUpstream("secondary", new Uri("https://secondary.example.com/"), 1));
        var health = new SiteHealthRegistry();
        health.MarkHealthy(site.Id, "primary");
        health.MarkHealthy(site.Id, "secondary");

        var selected = Enumerable.Range(0, 1_001)
            .Select(_ => health.Select(site)!.Id)
            .ToArray();

        Assert.All(selected, id => Assert.Equal("primary", id));
    }

    [Fact]
    public void Normalizes_site_and_upstream_health_state_keys()
    {
        var site = Site(new SiteUpstream("primary", new Uri("https://primary.example.com/")));
        var health = new SiteHealthRegistry();

        health.MarkUnhealthy(" SITE-A ", " PRIMARY ", "timeout");

        Assert.Equal("timeout", health.Get("site-a", "primary")?.Reason);
        Assert.Null(health.Select(site));
    }

    [Fact]
    public void Select_normalizes_the_site_upstream_id_before_health_state_lookup()
    {
        var site = Site(new SiteUpstream(" PRIMARY ", new Uri("https://primary.example.com/")));
        var health = new SiteHealthRegistry();
        health.MarkUnhealthy(site.Id, "primary", "timeout");

        Assert.Null(health.Select(site));
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

    [Fact]
    public void Evicts_the_least_recently_used_site_when_global_state_bound_is_reached()
    {
        var oldSite = Site("site-old", new SiteUpstream("primary", new Uri("https://old.example.com/")));
        var recentSite = Site("site-recent", new SiteUpstream("primary", new Uri("https://recent.example.com/")));
        var newSite = Site("site-new", new SiteUpstream("primary", new Uri("https://new.example.com/")));
        var health = new SiteHealthRegistry(maxSites: 2);

        health.MarkHealthy(oldSite.Id, "primary");
        health.MarkHealthy(recentSite.Id, "primary");
        _ = health.Get(oldSite.Id, "primary");
        health.MarkHealthy(newSite.Id, "primary");

        Assert.NotNull(health.Get(oldSite.Id, "primary"));
        Assert.Null(health.Get(recentSite.Id, "primary"));
        Assert.NotNull(health.Get(newSite.Id, "primary"));
    }

    private static GatewaySite Site(params SiteUpstream[] upstreams) => Site("site-a", upstreams);

    private static GatewaySite Site(string id, params SiteUpstream[] upstreams) =>
        new(id, true, [$"{id}.example.com"], ["/"], upstreams, "default",
            new SiteRateLimitPolicy(), new SiteEgressPolicy(upstreams.Select(x => x.Address.Host).ToArray(), new[] { 443 }, new[] { "https" }));
}
