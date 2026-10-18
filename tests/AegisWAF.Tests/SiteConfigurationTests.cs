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

    [Fact]
    public void Production_requires_explicit_https_egress_policy_per_site()
    {
        var config = Config(new Dictionary<string, string?>
        {
            ["Waf:Deployment:Enforce"] = "true",
            ["Sites:site-a:Enabled"] = "true",
            ["Sites:site-a:Hostnames:0"] = "app.example.com",
            ["Sites:site-a:Upstreams:0:Address"] = "http://origin.example.com/"
        });

        var errors = SiteConfigurationValidator.Validate(config);

        Assert.Contains(errors, error => error.Contains("egress", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Rejects_malformed_hostname_instead_of_silently_dropping_it()
    {
        var config = Config(new Dictionary<string, string?>
        {
            ["Sites:site-a:Hostnames:0"] = "not a hostname",
            ["Sites:site-a:Upstreams:0:Address"] = "https://origin.example.com/"
        });

        var errors = SiteConfigurationValidator.Validate(config);

        Assert.Contains(errors, error => error.Contains("hostname", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Production_requires_each_upstream_to_be_covered_by_site_egress_policy()
    {
        var config = Config(new Dictionary<string, string?>
        {
            ["Waf:Deployment:Enforce"] = "true",
            ["Sites:site-a:Hostnames:0"] = "app.example.com",
            ["Sites:site-a:Upstreams:0:Address"] = "https://origin.example.com/",
            ["Sites:site-a:Egress:AllowedHosts:0"] = "other.example.com",
            ["Sites:site-a:Egress:AllowedPorts:0"] = "443",
            ["Sites:site-a:Egress:AllowedSchemes:0"] = "https"
        });

        var errors = SiteConfigurationValidator.Validate(config);

        Assert.Contains(errors, error => error.Contains("cover", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Rejects_invalid_per_site_health_policy_bounds()
    {
        var config = Config(new Dictionary<string, string?>
        {
            ["Sites:site-a:Hostnames:0"] = "app.example.com",
            ["Sites:site-a:Upstreams:0:Address"] = "https://origin.example.com/",
            ["Sites:site-a:Health:Path"] = "health?unsafe=true",
            ["Sites:site-a:Health:IntervalSeconds"] = "2",
            ["Sites:site-a:Health:TimeoutSeconds"] = "3"
        });

        var errors = SiteConfigurationValidator.Validate(config);

        Assert.Contains(errors, error => error.Contains("health path", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("health timeout", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Production_rejects_invalid_per_site_rate_limits_instead_of_silent_clamping()
    {
        var config = Config(new Dictionary<string, string?>
        {
            ["Waf:Deployment:Enforce"] = "true",
            ["Sites:site-a:Hostnames:0"] = "app.example.com",
            ["Sites:site-a:Upstreams:0:Address"] = "https://origin.example.com/",
            ["Sites:site-a:Egress:AllowedHosts:0"] = "origin.example.com",
            ["Sites:site-a:Egress:AllowedPorts:0"] = "443",
            ["Sites:site-a:Egress:AllowedSchemes:0"] = "https",
            ["Sites:site-a:RateLimit:Requests"] = "0",
            ["Sites:site-a:RateLimit:WindowSeconds"] = "3601",
            ["Sites:site-a:RateLimit:GlobalRequests"] = "-1"
        });

        var errors = SiteConfigurationValidator.Validate(config);

        Assert.Contains(errors, error => error.Contains("rate requests", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("rate window", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("global rate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Production_rejects_wildcard_or_invalid_per_site_egress_entries()
    {
        var config = Config(new Dictionary<string, string?>
        {
            ["Waf:Deployment:Enforce"] = "true",
            ["Sites:site-a:Hostnames:0"] = "app.example.com",
            ["Sites:site-a:Upstreams:0:Address"] = "https://origin.example.com/",
            ["Sites:site-a:Egress:AllowedHosts:0"] = "*",
            ["Sites:site-a:Egress:AllowedHosts:1"] = "not a host",
            ["Sites:site-a:Egress:AllowedPorts:0"] = "70000",
            ["Sites:site-a:Egress:AllowedSchemes:0"] = "https"
        });

        var errors = SiteConfigurationValidator.Validate(config);

        Assert.Contains(errors, error => error.Contains("egress host", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("egress port", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Production_rejects_upstream_weight_outside_bounded_range()
    {
        var config = Config(new Dictionary<string, string?>
        {
            ["Waf:Deployment:Enforce"] = "true",
            ["Sites:site-a:Hostnames:0"] = "app.example.com",
            ["Sites:site-a:Upstreams:primary:Address"] = "https://origin.example.com/",
            ["Sites:site-a:Upstreams:primary:Weight"] = "0",
            ["Sites:site-a:Egress:AllowedHosts:0"] = "origin.example.com",
            ["Sites:site-a:Egress:AllowedPorts:0"] = "443",
            ["Sites:site-a:Egress:AllowedSchemes:0"] = "https"
        });

        var errors = SiteConfigurationValidator.Validate(config);

        Assert.Contains(errors, error => error.Contains("weight", StringComparison.OrdinalIgnoreCase));
    }
}
