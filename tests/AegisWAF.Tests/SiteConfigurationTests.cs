using AegisWAF.Gateway.Sites;
using Microsoft.Extensions.Configuration;

namespace AegisWAF.Tests;

public sealed class SiteConfigurationTests
{
    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void Loads_multiple_sites_with_normalized_hostnames_and_upstreams()
    {
        var config = Config(new Dictionary<string, string?>
        {
            ["Sites:site-a:Enabled"] = "true",
            ["Sites:site-a:Hostnames:0"] = " APP-A.EXAMPLE.COM ",
            ["Sites:site-a:PathPrefixes:0"] = "/api/",
            ["Sites:site-a:Upstreams:0:Id"] = "primary",
            ["Sites:site-a:Upstreams:0:Address"] = "https://origin-a.example.com/",
            ["Sites:site-b:Enabled"] = "true",
            ["Sites:site-b:Hostnames:0"] = "app-b.example.com",
            ["Sites:site-b:Upstreams:0:Id"] = "primary",
            ["Sites:site-b:Upstreams:0:Address"] = "https://origin-b.example.com/"
        });

        var snapshot = SiteConfigurationLoader.Load(config);

        Assert.Equal(["site-a", "site-b"], snapshot.Sites.Select(x => x.Id));
        Assert.Equal("app-a.example.com", snapshot.Sites[0].Hostnames.Single());
        Assert.Equal("/api", snapshot.Sites[0].PathPrefixes.Single());
        Assert.Equal("https://origin-b.example.com/", snapshot.Sites[1].Upstreams.Single().Address.ToString());
    }

    [Fact]
    public void Legacy_reverse_proxy_configuration_becomes_default_site()
    {
        var config = Config(new Dictionary<string, string?>
        {
            ["ReverseProxy:Clusters:appcluster:Destinations:app1:Address"] = "http://localhost:8080/"
        });

        var snapshot = SiteConfigurationLoader.Load(config);

        var site = Assert.Single(snapshot.Sites);
        Assert.Equal("default-site", site.Id);
        Assert.Equal("http://localhost:8080/", site.Upstreams.Single().Address.ToString());
        Assert.True(snapshot.UsedLegacyConfiguration);
    }

    [Fact]
    public void Rejects_duplicate_active_hostnames()
    {
        var config = Config(new Dictionary<string, string?>
        {
            ["Sites:first:Enabled"] = "true",
            ["Sites:first:Hostnames:0"] = "shared.example.com",
            ["Sites:first:Upstreams:0:Address"] = "https://origin-a.example.com/",
            ["Sites:second:Enabled"] = "true",
            ["Sites:second:Hostnames:0"] = "SHARED.EXAMPLE.COM.",
            ["Sites:second:Upstreams:0:Address"] = "https://origin-b.example.com/"
        });

        var errors = SiteConfigurationValidator.Validate(config);

        Assert.Contains(errors, error => error.Contains("hostname", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Rejects_upstream_credentials_and_non_http_schemes()
    {
        var config = Config(new Dictionary<string, string?>
        {
            ["Sites:site-a:Enabled"] = "true",
            ["Sites:site-a:Hostnames:0"] = "app.example.com",
            ["Sites:site-a:Upstreams:0:Address"] = "ftp://user:pass@example.com/"
        });

        var errors = SiteConfigurationValidator.Validate(config);

        Assert.Contains(errors, error => error.Contains("HTTP(S)", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("credentials", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Production_requires_an_active_site_with_explicit_hostname_and_upstream()
    {
        var config = Config(new Dictionary<string, string?>
        {
            ["Waf:Deployment:Enforce"] = "true",
            ["Sites:site-a:Enabled"] = "true"
        });

        var errors = SiteConfigurationValidator.Validate(config);

        Assert.Contains(errors, error => error.Contains("hostname", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("upstream", StringComparison.OrdinalIgnoreCase));
    }
}
