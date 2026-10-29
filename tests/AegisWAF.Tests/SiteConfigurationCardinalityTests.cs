using AegisWAF.Gateway.Sites;
using Microsoft.Extensions.Configuration;

namespace AegisWAF.Tests;

public sealed class SiteConfigurationCardinalityTests
{
    [Fact]
    public void Rejects_more_sites_than_the_configured_snapshot_limit()
    {
        var configuration = Config(new Dictionary<string, string?>
        {
            ["Waf:Sites:MaxSites"] = "2"
        });
        var sites = new[] { Site("site-a"), Site("site-b"), Site("site-c") };

        var errors = SiteConfigurationValidator.ValidateSnapshot(
            new SiteConfigurationSnapshot(sites, false), configuration);

        Assert.Contains(errors, error => error.Contains("at most 2 Sites", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Rejects_more_configured_site_sections_than_the_configured_limit()
    {
        var configuration = Config(new Dictionary<string, string?>
        {
            ["Waf:Sites:MaxSites"] = "2",
            ["Sites:site-a:Hostnames:0"] = "a.example.com",
            ["Sites:site-a:Upstreams:0:Address"] = "https://a-origin.example.com/",
            ["Sites:site-b:Hostnames:0"] = "b.example.com",
            ["Sites:site-b:Upstreams:0:Address"] = "https://b-origin.example.com/",
            ["Sites:site-c:Hostnames:0"] = "c.example.com",
            ["Sites:site-c:Upstreams:0:Address"] = "https://c-origin.example.com/"
        });

        var errors = SiteConfigurationValidator.Validate(configuration);

        Assert.Contains(errors, error => error.Contains("at most 2 Sites", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Source_bounds_database_snapshot_loading_before_materializing_unlimited_rows()
    {
        var root = FindRepoRoot();
        var validator = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "Sites", "SiteConfiguration.cs"));
        var store = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "Sites", "SiteDatabaseStore.cs"));

        Assert.Contains("MaxSites", validator, StringComparison.Ordinal);
        Assert.Contains("LIMIT @max_sites_plus_one", store, StringComparison.Ordinal);
        Assert.Contains("more than", store, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_oversized_per_site_collections_before_binding_runtime_routes()
    {
        var values = new Dictionary<string, string?>
        {
            ["Sites:shop:Hostnames:0"] = "shop-0.example.com",
            ["Sites:shop:PathPrefixes:0"] = "/",
            ["Sites:shop:Upstreams:0:Address"] = "https://origin-0.example.com/"
        };
        for (var index = 1; index <= 32; index++)
        {
            values[$"Sites:shop:Hostnames:{index}"] = $"shop-{index}.example.com";
            values[$"Sites:shop:Upstreams:{index}:Address"] = $"https://origin-{index}.example.com/";
        }
        for (var index = 1; index <= 64; index++)
            values[$"Sites:shop:PathPrefixes:{index}"] = $"/path-{index}";

        var errors = SiteConfigurationValidator.Validate(Config(values));

        Assert.Contains(errors, error => error.Contains("at most 32 hostnames", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("at most 64 path prefixes", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("at most 32 upstreams", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Rejects_more_generated_routes_than_the_global_runtime_limit()
    {
        var configuration = Config(new Dictionary<string, string?>
        {
            ["Waf:Sites:MaxGeneratedRoutes"] = "3",
            ["Sites:shop:Hostnames:0"] = "shop-a.example.com",
            ["Sites:shop:Hostnames:1"] = "shop-b.example.com",
            ["Sites:shop:PathPrefixes:0"] = "/api",
            ["Sites:shop:PathPrefixes:1"] = "/admin",
            ["Sites:shop:Upstreams:0:Address"] = "https://origin.example.com/"
        });

        var errors = SiteConfigurationValidator.Validate(configuration);

        Assert.Contains(errors, error => error.Contains("generate at most 3 routes", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Rejects_more_generated_destinations_than_the_global_runtime_limit()
    {
        var configuration = Config(new Dictionary<string, string?>
        {
            ["Waf:Sites:MaxGeneratedDestinations"] = "1"
        });
        var site = new GatewaySite("shop", true, ["shop.example.com"], ["/"],
            [
                new SiteUpstream("primary", new Uri("https://origin-a.example.com/")),
                new SiteUpstream("secondary", new Uri("https://origin-b.example.com/"))
            ], "default", new SiteRateLimitPolicy(),
            new SiteEgressPolicy(["origin-a.example.com", "origin-b.example.com"], [443], ["https"]));

        var errors = SiteConfigurationValidator.ValidateSnapshot(
            new SiteConfigurationSnapshot([site], false), configuration);

        Assert.Contains(errors, error => error.Contains("generate at most 1 destinations", StringComparison.OrdinalIgnoreCase));
    }

    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static GatewaySite Site(string id) =>
        new(id, true, [$"{id}.example.com"], ["/"],
            [new SiteUpstream("primary", new Uri($"https://{id}-origin.example.com/"))], "default",
            new SiteRateLimitPolicy(), new SiteEgressPolicy([$"{id}-origin.example.com"], [443], ["https"]));

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
