using AegisWAF.Gateway.Sites;

namespace AegisWAF.Tests;

public sealed class SiteMatcherTests
{
    [Fact]
    public void Matches_hostname_case_insensitively_and_ignores_default_port()
    {
        var site = Site("site-a", "app-a.example.com", ["/"]);
        var snapshot = new SiteConfigurationSnapshot([site], false);

        var match = SiteMatcher.Match(snapshot, "APP-A.EXAMPLE.COM:443", "/home");

        Assert.NotNull(match);
        Assert.Equal("site-a", match.Site.Id);
        Assert.Equal("app-a.example.com", match.Hostname);
    }

    [Fact]
    public void Selects_the_longest_matching_path_prefix()
    {
        var site = Site("site-a", "app-a.example.com", ["/", "/api", "/api/admin"]);
        var snapshot = new SiteConfigurationSnapshot([site], false);

        var match = SiteMatcher.Match(snapshot, "app-a.example.com", "/api/admin/users");

        Assert.NotNull(match);
        Assert.Equal("/api/admin", match.PathPrefix);
    }

    [Fact]
    public void Does_not_match_unknown_hosts_or_path_prefix_boundaries()
    {
        var site = Site("site-a", "app-a.example.com", ["/api"]);
        var snapshot = new SiteConfigurationSnapshot([site], false);

        Assert.Null(SiteMatcher.Match(snapshot, "unknown.example.com", "/api"));
        Assert.Null(SiteMatcher.Match(snapshot, "app-a.example.com", "/apix"));
    }

    [Fact]
    public void Disabled_sites_are_not_matchable()
    {
        var site = Site("site-a", "app-a.example.com", ["/"], enabled: false);
        var snapshot = new SiteConfigurationSnapshot([site], false);

        Assert.Null(SiteMatcher.Match(snapshot, "app-a.example.com", "/"));
    }

    private static GatewaySite Site(string id, string host, string[] prefixes, bool enabled = true) =>
        new(id, enabled, [host], prefixes, [new SiteUpstream("primary", new Uri("https://origin.example.com/"))], "default",
            new SiteRateLimitPolicy(), new SiteEgressPolicy(["origin.example.com"], [443], ["https"]));
}
