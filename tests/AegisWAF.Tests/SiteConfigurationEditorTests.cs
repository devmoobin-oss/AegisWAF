using System.Text.Json.Nodes;
using AegisWAF.Gateway.Sites;
using Microsoft.Extensions.Configuration;

namespace AegisWAF.Tests;

public sealed class SiteConfigurationEditorTests
{
    [Fact]
    public void Management_editor_uses_the_shared_site_id_validation_rule()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "Sites", "SiteConfigurationEditor.cs"));

        Assert.Contains("SiteConfigurationValidator.IsValidSiteId(id)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private static readonly Regex IdPattern", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Normalizes_multiple_upstreams_with_weights_and_preserves_policy_fields()
    {
        var request = new SiteManagementRequest(
            "shop", ["shop.example.com"], ["/"],
            "https://origin-a.example.com/", "default", [], [], [], true)
        {
            Upstreams =
            [
                new SiteUpstreamManagementRequest("primary", "https://origin-a.example.com/", 700),
                new SiteUpstreamManagementRequest("secondary", "https://origin-b.example.com/", 300)
            ],
            Health = new SiteHealthPolicy("/ready", 30, 5, 120),
            RateLimit = new SiteRateLimitPolicy(1200, 90, 40000)
        };

        Assert.True(SiteConfigurationEditor.TryNormalize(request, out var normalized, out var error), error);
        var site = SiteDatabaseStore.ToGatewaySite(normalized);

        Assert.Equal(["primary", "secondary"], site.Upstreams.Select(x => x.Id));
        Assert.Equal([700, 300], site.Upstreams.Select(x => x.Weight));
        Assert.Equal("/ready", site.Health!.Path);
        Assert.Equal(1200, site.RateLimit.Requests);
    }

    [Fact]
    public void Production_site_mutation_validation_rejects_http_upstream()
    {
        var request = new SiteManagementRequest(
            "shop", ["shop.example.com"], ["/"],
            "http://origin.example.com/", "default",
            ["origin.example.com"], [80], ["http"], true);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Waf:Deployment:Enforce"] = "true"
        }).Build();

        Assert.True(SiteConfigurationEditor.TryNormalize(request, out var normalized, out _));
        var candidate = SiteDatabaseStore.ToGatewaySite(normalized);

        var errors = SiteConfigurationValidator.ValidateSnapshot(
            new SiteConfigurationSnapshot([candidate], false), configuration);

        Assert.Contains(errors, error => error.Contains("HTTPS", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Creates_a_site_document_without_persisting_secrets()
    {
        var request = new SiteManagementRequest(
            "shop", ["shop.example.com"], ["/"],
            "https://origin.example.com/", "default",
            ["origin.example.com"], [443], ["https"], true);

        var site = SiteConfigurationEditor.ToJson(request);

        Assert.True(site["Enabled"]!.GetValue<bool>());
        Assert.Equal("shop.example.com", site["Hostnames"]![0]!.GetValue<string>());
        Assert.Equal("https://origin.example.com/", site["Upstreams"]!["primary"]!["Address"]!.GetValue<string>());
        Assert.Null(site["ConnectionStrings"]);
        Assert.Null(site["Password"]);
    }

    [Fact]
    public void Defaults_health_check_to_the_upstream_base_path_when_no_dedicated_health_endpoint_is_configured()
    {
        var request = new SiteManagementRequest(
            "donsak", ["localhost"], ["/donsak-i"],
            "http://localhost:3000/login/", "default",
            ["localhost"], [3000], ["http"], true);

        var site = SiteConfigurationEditor.ToJson(request);

        Assert.Equal("/login", site["Health"]!["Path"]!.GetValue<string>());
    }

    [Fact]
    public void Rejects_upstream_query_strings_and_fragments_at_the_api_boundary()
    {
        var request = new SiteManagementRequest(
            "shop", ["shop.example.com"], ["/"],
            "https://origin.example.com/base?tenant=secret#fragment", "default",
            ["origin.example.com"], [443], ["https"], true);

        Assert.False(SiteConfigurationEditor.TryNormalize(request, out _, out var error));
        Assert.Contains("query or fragment", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_unsafe_upstream_ids_at_the_api_boundary()
    {
        var request = new SiteManagementRequest(
            "shop", ["shop.example.com"], ["/"],
            "https://origin.example.com/", "default",
            ["origin.example.com"], [443], ["https"], true)
        {
            Upstreams = [new SiteUpstreamManagementRequest("primary/one", "https://origin.example.com/")]
        };

        Assert.False(SiteConfigurationEditor.TryNormalize(request, out _, out var error));
        Assert.Contains("upstream", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ID", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_invalid_hostname_instead_of_silently_dropping_it()
    {
        var request = new SiteManagementRequest(
            "shop", ["shop.example.com", "not a hostname"], ["/"],
            "https://origin.example.com/", "default",
            ["origin.example.com"], [443], ["https"], true);

        Assert.False(SiteConfigurationEditor.TryNormalize(request, out _, out var error));
        Assert.Contains("invalid hostname", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_invalid_explicit_egress_entries_instead_of_filtering_them()
    {
        var request = new SiteManagementRequest(
            "shop", ["shop.example.com"], ["/"],
            "https://origin.example.com/", "default",
            ["origin.example.com", "*"], [443], ["https"], true);

        Assert.False(SiteConfigurationEditor.TryNormalize(request, out _, out var error));
        Assert.Contains("egress host", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_out_of_range_or_duplicate_egress_ports_at_the_input_boundary()
    {
        var request = new SiteManagementRequest(
            "shop", ["shop.example.com"], ["/"],
            "https://origin.example.com/", "default",
            ["origin.example.com"], [443, 443], ["https"], true);

        Assert.False(SiteConfigurationEditor.TryNormalize(request, out _, out var error));
        Assert.Contains("egress ports", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_non_http_egress_schemes_at_the_input_boundary()
    {
        var request = new SiteManagementRequest(
            "shop", ["shop.example.com"], ["/"],
            "https://origin.example.com/", "default",
            ["origin.example.com"], [443], ["ftp"], true);

        Assert.False(SiteConfigurationEditor.TryNormalize(request, out _, out var error));
        Assert.Contains("egress schemes", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Merges_a_site_into_local_document_without_overwriting_other_sites()
    {
        var root = new JsonObject
        {
            ["Logging"] = new JsonObject { ["LogLevel"] = new JsonObject { ["Default"] = "Information" } },
            ["Sites"] = new JsonObject
            {
                ["existing"] = new JsonObject { ["Enabled"] = true }
            }
        };
        var request = new SiteManagementRequest(
            "new-site", ["new.example.com"], ["/"],
            "https://origin.example.com/", "default",
            ["origin.example.com"], [443], ["https"], true);

        SiteConfigurationEditor.Merge(root, request);

        Assert.NotNull(root["Logging"]);
        Assert.NotNull(root["Sites"]!["existing"]);
        Assert.NotNull(root["Sites"]!["new-site"]);
    }

    [Fact]
    public void Removes_a_site_as_a_disabled_override_so_base_configuration_cannot_reappear()
    {
        var root = new JsonObject
        {
            ["Sites"] = new JsonObject
            {
                ["existing"] = new JsonObject { ["Enabled"] = true }
            }
        };

        SiteConfigurationEditor.Remove(root, "existing");

        Assert.False(root["Sites"]!["existing"]!["Enabled"]!.GetValue<bool>());
    }

    [Fact]
    public void Detects_when_staged_site_differs_from_active_configuration()
    {
        var active = new GatewaySite(
            "shop", true, ["shop.example.com"], ["/"],
            [new SiteUpstream("primary", new Uri("https://origin.example.com/"))],
            "default", new SiteRateLimitPolicy(),
            new SiteEgressPolicy(["origin.example.com"], [443], ["https"]));

        Assert.True(SiteConfigurationEditor.AreEquivalent(active, active with { }));
        Assert.False(SiteConfigurationEditor.AreEquivalent(active, active with
        {
            Hostnames = ["www.example.com"]
        }));
    }

    [Fact]
    public void Database_site_mapping_uses_the_upstream_base_path_for_health_checks()
    {
        var request = new SiteManagementRequest(
            "donsak", ["localhost"], ["/donsak-i"],
            "http://localhost:3000/login/", "default",
            ["localhost"], [3000], ["http"], true);

        var site = SiteDatabaseStore.ToGatewaySite(request);

        Assert.Equal("/login", site.Health!.Path);
    }

    [Fact]
    public void Preserves_an_explicit_private_exception_for_development_health_probes()
    {
        var request = new SiteManagementRequest(
            "local-dev", ["local-dev.localhost"], ["/"],
            "http://127.0.0.1:4000/", "default",
            ["127.0.0.1"], [4000], ["http"], true)
        {
            AllowPrivateExceptions = true
        };

        Assert.True(SiteConfigurationEditor.TryNormalize(request, out var normalized, out var error), error);
        var site = SiteDatabaseStore.ToGatewaySite(normalized);
        var json = SiteConfigurationEditor.ToJson(normalized);

        Assert.True(site.Egress.AllowPrivateExceptions);
        Assert.True(json["Egress"]!["AllowPrivateExceptions"]!.GetValue<bool>());
    }

    [Fact]
    public void Production_rejects_a_private_exception_even_when_the_form_requests_it()
    {
        var request = new SiteManagementRequest(
            "local-dev", ["local-dev.example.com"], ["/"],
            "http://127.0.0.1:4000/", "default",
            ["127.0.0.1"], [4000], ["http"], true)
        {
            AllowPrivateExceptions = true
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Waf:Deployment:Enforce"] = "true"
        }).Build();

        Assert.True(SiteConfigurationEditor.TryNormalize(request, out var normalized, out _));
        var errors = SiteConfigurationValidator.ValidateSnapshot(
            new SiteConfigurationSnapshot([SiteDatabaseStore.ToGatewaySite(normalized)], false), configuration);

        Assert.Contains(errors, error => error.Contains("private egress exceptions", StringComparison.OrdinalIgnoreCase));
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
