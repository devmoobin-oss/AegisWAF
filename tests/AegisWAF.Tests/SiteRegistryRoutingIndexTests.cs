using AegisWAF.Gateway.Sites;

namespace AegisWAF.Tests;

public sealed class SiteRegistryRoutingIndexTests
{
    [Fact]
    public void Registry_resolves_the_longest_path_from_the_precomputed_hostname_index()
    {
        var site = Site("site-a", "app-a.example.test", ["/", "/api", "/api/admin"]);
        var registry = new SiteRegistry(new SiteConfigurationSnapshot([site], false));

        var match = registry.Resolve("APP-A.EXAMPLE.TEST:443", "/api/admin/users");

        Assert.NotNull(match);
        Assert.Equal("site-a", match!.Site.Id);
        Assert.Equal("/api/admin", match.PathPrefix);
    }

    [Fact]
    public void Registry_hostname_lookup_uses_the_same_index_for_pathless_telemetry_attribution()
    {
        var site = Site("site-a", "app-a.example.test", ["/api"]);
        var registry = new SiteRegistry(new SiteConfigurationSnapshot([site], false));

        var match = registry.MatchHostname("app-a.example.test:5080");

        Assert.NotNull(match);
        Assert.Equal("site-a", match!.Site.Id);
        Assert.Equal("app-a.example.test", match.Hostname);
    }

    [Fact]
    public void Registry_source_builds_and_consumes_a_hostname_index()
    {
        var root = FindRepoRoot();
        var registry = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "Sites", "SiteRegistry.cs"));
        var gateway = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "GatewayApp.cs"));

        Assert.Contains("ByHostname", registry, StringComparison.Ordinal);
        Assert.Contains("SiteMatcher.Match(state.ByHostname", registry, StringComparison.Ordinal);
        Assert.Contains("registry.MatchHostname(ctx.Request.Host.Value)", gateway, StringComparison.Ordinal);
    }

    private static GatewaySite Site(string id, string host, string[] prefixes) =>
        new(id, true, [host], prefixes,
            [new SiteUpstream("primary", new Uri("https://origin.example.test/"))], "default",
            new SiteRateLimitPolicy(), new SiteEgressPolicy(["origin.example.test"], [443], ["https"]));

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AegisWAF.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
