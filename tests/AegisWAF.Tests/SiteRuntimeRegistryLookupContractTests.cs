namespace AegisWAF.Tests;

public sealed class SiteRuntimeRegistryLookupContractTests
{
    [Fact]
    public void Registry_lookup_is_case_insensitive_and_returns_no_value_for_missing_sites()
    {
        var site = new AegisWAF.Gateway.Sites.GatewaySite(
            "Shop-Prod", true, ["shop.example.test"], ["/"],
            [new AegisWAF.Gateway.Sites.SiteUpstream("primary", new Uri("https://origin.example.test/"))],
            "default", new AegisWAF.Gateway.Sites.SiteRateLimitPolicy(),
            new AegisWAF.Gateway.Sites.SiteEgressPolicy(["origin.example.test"], [443], ["https"]));
        var registry = new AegisWAF.Gateway.Sites.SiteRegistry(
            new AegisWAF.Gateway.Sites.SiteConfigurationSnapshot([site], false));

        Assert.True(registry.TryGetById(" shop-prod ", out var found));
        Assert.Same(site, found);
        Assert.False(registry.TryGetById("missing", out var absent));
        Assert.Null(absent);
    }

    [Fact]
    public void Health_endpoint_uses_the_registry_index_instead_of_scanning_all_sites()
    {
        var root = FindRepoRoot();
        var gateway = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "GatewayApp.cs"));
        var start = gateway.IndexOf("app.MapPost(\"/api/sites/{id}/health\"", StringComparison.Ordinal);
        var end = gateway.IndexOf("app.MapGet(\"/api/sites/{id}/health/history\"", start, StringComparison.Ordinal);

        Assert.True(start >= 0 && end > start, "Could not isolate the Site health endpoint.");
        var endpoint = gateway[start..end];
        Assert.Contains("registry.TryGetById(site.Id, out var active)", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("registry.Current.Sites.FirstOrDefault", endpoint, StringComparison.Ordinal);
    }

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
