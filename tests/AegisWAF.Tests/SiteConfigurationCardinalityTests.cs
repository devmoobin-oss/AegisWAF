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
