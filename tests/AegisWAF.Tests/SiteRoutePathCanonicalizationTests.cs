using AegisWAF.Gateway.Sites;

namespace AegisWAF.Tests;

public sealed class SiteRoutePathCanonicalizationTests
{
    [Fact]
    public void Matcher_canonicalizes_a_trailing_slash_prefix_before_matching()
    {
        var site = Site("shop", "/api/");

        var match = SiteMatcher.Match(new SiteConfigurationSnapshot([site], false), "shop.example.com", "/api/orders");

        Assert.NotNull(match);
        Assert.Equal("/api", match.PathPrefix);
    }

    [Fact]
    public void Proxy_routes_do_not_emit_a_double_slash_for_a_trailing_slash_prefix()
    {
        var site = Site("shop", "/api/");

        var values = SiteProxyConfiguration.Build(new SiteConfigurationSnapshot([site], false));

        Assert.Equal("/api/{**remainder}", values["ReverseProxy:Routes:site-shop-" + RouteSuffix("shop.example.com", "/api") + ":Match:Path"]);
    }

    [Fact]
    public void Persistence_codec_canonicalizes_a_trailing_slash_prefix()
    {
        var row = new SitePersistenceRow(
            "shop", true, ["shop.example.com"], ["/api/"],
            ["primary"], ["https://origin.example.com/"], [100], "default",
            ["origin.example.com"], [443], ["https"], false, "/", 10, 3, 30, 300, 60, 10000);

        var restored = SitePersistenceCodec.FromRow(row);

        Assert.Equal(["/api"], restored.PathPrefixes);
    }

    [Fact]
    public void Persistence_codec_rejects_prefixes_that_collide_after_normalization()
    {
        var row = new SitePersistenceRow(
            "shop", true, ["shop.example.com"], ["/api", "/api/"],
            ["primary"], ["https://origin.example.com/"], [100], "default",
            ["origin.example.com"], [443], ["https"], false, "/", 10, 3, 30, 300, 60, 10000);

        var error = Assert.Throws<InvalidDataException>(() => SitePersistenceCodec.FromRow(row));

        Assert.Contains("duplicate path prefixes", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static GatewaySite Site(string id, string prefix) => new(
        id,
        true,
        ["shop.example.com"],
        [prefix],
        [new SiteUpstream("primary", new Uri("https://origin.example.com/"))],
        "default",
        new SiteRateLimitPolicy(),
        new SiteEgressPolicy(["origin.example.com"], [443], ["https"]));

    private static string RouteSuffix(string hostname, string prefix)
    {
        var key = hostname + "\0" + prefix;
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)))[..10].ToLowerInvariant();
    }
}
