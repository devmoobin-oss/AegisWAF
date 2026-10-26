using AegisWAF.Gateway.Sites;
using Microsoft.Extensions.Configuration;

namespace AegisWAF.Tests;

public sealed class SiteRuntimeActivationTests
{
    [Fact]
    public void Activation_replaces_routes_and_registry_as_one_runtime_snapshot()
    {
        var oldSite = Site("old", "old.example.com");
        var newSite = Site("new", "new.example.com");
        var configuration = Configuration();
        var registry = new SiteRegistry(new SiteConfigurationSnapshot([oldSite], false));
        var provider = new SiteRuntimeConfigurationProvider(SiteProxyConfiguration.Build(registry.Current, configuration));
        var coordinator = new SiteRuntimeCoordinator(registry, provider, configuration);

        coordinator.Activate(new SiteConfigurationSnapshot([newSite], false));

        Assert.Equal("new", registry.Current.Sites.Single().Id);
        Assert.Contains(provider.Current, item => item.Key.Contains("site-new", StringComparison.Ordinal));
        Assert.DoesNotContain(provider.Current, item => item.Key.Contains("site-old", StringComparison.Ordinal));
    }

    [Fact]
    public void Invalid_activation_preserves_the_previous_registry_and_routes()
    {
        var oldSite = Site("old", "old.example.com");
        var invalidSite = Site("invalid", "old.example.com");
        var configuration = Configuration();
        var registry = new SiteRegistry(new SiteConfigurationSnapshot([oldSite], false));
        var provider = new SiteRuntimeConfigurationProvider(SiteProxyConfiguration.Build(registry.Current, configuration));
        var coordinator = new SiteRuntimeCoordinator(registry, provider, configuration);
        var previousProvider = provider.Current;

        Assert.ThrowsAny<InvalidOperationException>(() => coordinator.Activate(new SiteConfigurationSnapshot([oldSite, invalidSite], false)));

        Assert.Equal("old", registry.Current.Sites.Single().Id);
        Assert.Same(previousProvider, provider.Current);
    }

    private static IConfiguration Configuration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Waf:Deployment:Enforce"] = "false" })
        .Build();

    private static GatewaySite Site(string id, string hostname) => new(
        id,
        true,
        [hostname],
        ["/"],
        [new SiteUpstream("primary", new Uri("http://origin.example.com/"))],
        "default",
        new SiteRateLimitPolicy(),
        new SiteEgressPolicy(["origin.example.com"], [80], ["http"]));
}
