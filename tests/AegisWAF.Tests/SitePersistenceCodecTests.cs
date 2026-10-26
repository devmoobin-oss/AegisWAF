using AegisWAF.Gateway.Sites;

namespace AegisWAF.Tests;

public sealed class SitePersistenceCodecTests
{
    [Fact]
    public void Persistence_round_trip_preserves_health_rate_limit_and_multiple_upstreams()
    {
        var site = new GatewaySite(
            "shop", true, ["shop.example.com"], ["/api"],
            [
                new SiteUpstream("primary", new Uri("https://origin-a.example.com/base/"), 700),
                new SiteUpstream("secondary", new Uri("https://origin-b.example.com/"), 300)
            ],
            "default",
            new SiteRateLimitPolicy(1200, 90, 40000),
            new SiteEgressPolicy(["origin-a.example.com", "origin-b.example.com"], [443], ["https"], AllowPrivateExceptions: true),
            new SiteHealthPolicy("/ready", 30, 5, 120));

        var row = SitePersistenceCodec.ToRow(site);
        var restored = SitePersistenceCodec.FromRow(row);

        Assert.True(SiteConfigurationEditor.AreEquivalent(site, restored));
    }

    [Fact]
    public void Persistence_codec_rejects_mismatched_upstream_arrays_instead_of_indexing_past_the_end()
    {
        var row = new SitePersistenceRow(
            "broken", true, ["broken.example.com"], ["/"],
            ["primary", "secondary"], ["https://origin.example.com/"], [100], "default",
            ["origin.example.com"], [443], ["https"], false, "/", 10, 3, 30, 300, 60, 10000);

        var error = Assert.Throws<InvalidDataException>(() => SitePersistenceCodec.FromRow(row));

        Assert.Contains("same length", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Persistence_codec_rejects_credentials_in_persisted_upstream()
    {
        var row = new SitePersistenceRow(
            "broken", true, ["broken.example.com"], ["/"],
            ["primary"], ["https://user:password@origin.example.com/"], [100], "default",
            ["origin.example.com"], [443], ["https"], false, "/", 10, 3, 30, 300, 60, 10000);

        var error = Assert.Throws<InvalidDataException>(() => SitePersistenceCodec.FromRow(row));

        Assert.Contains("credentials", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
