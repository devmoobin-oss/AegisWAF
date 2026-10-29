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

    [Fact]
    public void Persistence_codec_rejects_invalid_or_duplicate_egress_entries_before_materialization()
    {
        var cases = new[]
        {
            (new[] { "origin.example.com", "ORIGIN.EXAMPLE.COM." }, new[] { 443 }, new[] { "https" }, "duplicate egress host"),
            (new[] { "origin.example.com" }, new[] { 443, 443 }, new[] { "https" }, "duplicate egress port"),
            (new[] { "origin.example.com" }, new[] { 443 }, new[] { "https", "HTTPS" }, "duplicate egress scheme"),
            (new[] { "*" }, new[] { 443 }, new[] { "https" }, "egress host"),
            (new[] { "not a host" }, new[] { 443 }, new[] { "https" }, "egress host"),
            (new[] { "origin.example.com" }, new[] { 443 }, new[] { "ftp" }, "egress scheme")
        };

        foreach (var testCase in cases)
        {
            var row = new SitePersistenceRow(
                "broken", true, ["broken.example.com"], ["/"],
                ["primary"], ["https://origin.example.com/"], [100], "default",
                testCase.Item1, testCase.Item2, testCase.Item3, false, "/", 10, 3, 30, 300, 60, 10000);

            var error = Assert.Throws<InvalidDataException>(() => SitePersistenceCodec.FromRow(row));

            Assert.Contains(testCase.Item4, error.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Persistence_codec_rejects_oversized_site_collections_before_materialization()
    {
        var cases = new[]
        {
            (MakeRow(hostnames: Enumerable.Range(0, 33).Select(x => $"host-{x}.example.com").ToArray()), "hostnames"),
            (MakeRow(pathPrefixes: Enumerable.Range(0, 65).Select(x => $"/path-{x}").ToArray()), "path prefixes"),
            (MakeRow(
                upstreamIds: Enumerable.Range(0, 33).Select(x => $"upstream-{x}").ToArray(),
                upstreamAddresses: Enumerable.Range(0, 33).Select(x => $"https://origin-{x}.example.com/").ToArray(),
                upstreamWeights: Enumerable.Repeat(100, 33).ToArray()), "upstreams"),
            (MakeRow(egressAllowedHosts: Enumerable.Range(0, 65).Select(x => $"origin-{x}.example.com").ToArray()), "egress hosts"),
            (MakeRow(egressAllowedPorts: Enumerable.Range(0, 33).Select(x => 4000 + x).ToArray()), "egress ports"),
            (MakeRow(egressAllowedSchemes: ["https", "http", "https", "http", "https"]), "egress schemes")
        };

        foreach (var testCase in cases)
        {
            var error = Assert.Throws<InvalidDataException>(() => SitePersistenceCodec.FromRow(testCase.Item1));

            Assert.Contains($"at most", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(testCase.Item2, error.Message, StringComparison.OrdinalIgnoreCase);
        }

        static SitePersistenceRow MakeRow(
            string[]? hostnames = null,
            string[]? pathPrefixes = null,
            string[]? upstreamIds = null,
            string[]? upstreamAddresses = null,
            int[]? upstreamWeights = null,
            string[]? egressAllowedHosts = null,
            int[]? egressAllowedPorts = null,
            string[]? egressAllowedSchemes = null) => new(
                "broken",
                true,
                hostnames ?? ["broken.example.com"],
                pathPrefixes ?? ["/"],
                upstreamIds ?? ["primary"],
                upstreamAddresses ?? ["https://origin.example.com/"],
                upstreamWeights ?? [100],
                "default",
                egressAllowedHosts ?? ["origin.example.com"],
                egressAllowedPorts ?? [443],
                egressAllowedSchemes ?? ["https"],
                false,
                "/",
                10,
                3,
                30,
                300,
                60,
                10000);
    }

    [Fact]
    public void Persistence_codec_rejects_invalid_site_identity_and_empty_required_collections()
    {
        var cases = new[]
        {
            (MakeRow(siteId: "bad site"), "invalid ID"),
            (MakeRow(hostnames: []), "hostname"),
            (MakeRow(pathPrefixes: []), "path prefix")
        };

        foreach (var testCase in cases)
        {
            var error = Assert.Throws<InvalidDataException>(() => SitePersistenceCodec.FromRow(testCase.Item1));

            Assert.Contains(testCase.Item2, error.Message, StringComparison.OrdinalIgnoreCase);
        }

        static SitePersistenceRow MakeRow(
            string siteId = "broken",
            string[]? hostnames = null,
            string[]? pathPrefixes = null) => new(
                siteId,
                true,
                hostnames ?? ["broken.example.com"],
                pathPrefixes ?? ["/"],
                ["primary"],
                ["https://origin.example.com/"],
                [100],
                "default",
                ["origin.example.com"],
                [443],
                ["https"],
                false,
                "/",
                10,
                3,
                30,
                300,
                60,
                10000);
    }

    [Fact]
    public void Persistence_codec_canonicalizes_a_valid_site_id_before_materialization()
    {
        var row = new SitePersistenceRow(
            "  SHOP  ", true, ["shop.example.com"], ["/"],
            ["primary"], ["https://origin.example.com/"], [100], "default",
            ["origin.example.com"], [443], ["https"], false, "/", 10, 3, 30, 300, 60, 10000);

        var restored = SitePersistenceCodec.FromRow(row);

        Assert.Equal("shop", restored.Id);
    }
}
