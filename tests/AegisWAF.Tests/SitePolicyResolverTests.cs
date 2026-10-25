using AegisWAF.Gateway.Sites;
using Microsoft.Extensions.Configuration;

namespace AegisWAF.Tests;

public sealed class SitePolicyResolverTests
{
    [Fact]
    public void Resolves_named_policy_as_an_immutable_site_snapshot()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Waf:Policies:checkout:DisabledRules:0"] = "xss-01",
            ["Waf:Policies:checkout:DisabledRules:1"] = "anomaly-01"
        }).Build();
        var resolver = new SitePolicyResolver(configuration);
        var site = new GatewaySite("shop", true, ["shop.example"], ["/"], [], "checkout", new SiteRateLimitPolicy(), new SiteEgressPolicy([], [], []));

        var policy = resolver.Resolve(site);

        Assert.Equal("checkout", policy.Name);
        Assert.Contains("xss-01", policy.DisabledRuleIds);
        Assert.Contains("anomaly-01", policy.DisabledRuleIds);
        Assert.Throws<NotSupportedException>(() =>
        {
            ((ISet<string>)policy.DisabledRuleIds).Add("sqli-01");
        });
    }

    [Fact]
    public void Unknown_policy_uses_global_fallback_without_cross_site_state()
    {
        var resolver = new SitePolicyResolver(new ConfigurationBuilder().Build());
        var site = new GatewaySite("shop", true, ["shop.example"], ["/"], [], "missing", new SiteRateLimitPolicy(), new SiteEgressPolicy([], [], []));

        var policy = resolver.Resolve(site);

        Assert.Equal("global", policy.Name);
        Assert.Empty(policy.DisabledRuleIds);
    }

    [Fact]
    public void Site_policy_can_only_reduce_global_rule_coverage_for_that_site()
    {
        var rules = new SecurityRules(new ConfigurationBuilder().Build());
        var policy = new SiteWafPolicy("checkout", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "xss-01" });

        Assert.Null(rules.Match("<script>alert(1)</script>", policy: policy));
        Assert.Equal("SQL Injection", rules.Match("' OR username = 'admin'", policy: policy));
    }

    [Fact]
    public void Direct_policy_construction_copies_mutable_input()
    {
        var disabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "xss-01" };
        var policy = new SiteWafPolicy("checkout", disabled);

        disabled.Add("sqli-01");

        Assert.Contains("xss-01", policy.DisabledRuleIds);
        Assert.DoesNotContain("sqli-01", policy.DisabledRuleIds);
        Assert.Throws<NotSupportedException>(() =>
        {
            ((ISet<string>)policy.DisabledRuleIds).Add("cmd-01");
        });
    }
}
