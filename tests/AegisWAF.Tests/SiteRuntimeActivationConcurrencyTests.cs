using AegisWAF.Gateway.Sites;
using Microsoft.Extensions.Configuration;

namespace AegisWAF.Tests;

public sealed class SiteRuntimeActivationConcurrencyTests
{
    [Fact]
    public async Task Database_snapshot_load_and_activation_are_serialized_as_one_operation()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Waf:Deployment:Enforce"] = "false" })
            .Build();
        var initial = Site("initial", "initial.example.com");
        var firstSnapshot = Site("first", "first.example.com");
        var secondSnapshot = Site("second", "second.example.com");
        var registry = new SiteRegistry(new SiteConfigurationSnapshot([initial], false));
        var provider = new SiteRuntimeConfigurationProvider(SiteProxyConfiguration.Build(registry.Current, configuration));
        var coordinator = new SiteRuntimeCoordinator(registry, provider, configuration);
        var firstLoadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstLoad = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = coordinator.ActivateFromSnapshotLoaderAsync(async _ =>
        {
            firstLoadStarted.SetResult();
            await releaseFirstLoad.Task;
            return (IReadOnlyList<GatewaySite>)[firstSnapshot];
        }, CancellationToken.None);
        await firstLoadStarted.Task;

        var second = coordinator.ActivateFromSnapshotLoaderAsync(
            _ => Task.FromResult<IReadOnlyList<GatewaySite>>([secondSnapshot]),
            CancellationToken.None);

        var completed = await Task.WhenAny(second, Task.Delay(TimeSpan.FromMilliseconds(100)));
        Assert.NotSame(second, completed);
        releaseFirstLoad.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal("second", registry.Current.Sites.Single().Id);
        Assert.Contains(provider.Current, item => item.Key.Contains("site-second", StringComparison.Ordinal));
        Assert.DoesNotContain(provider.Current, item => item.Key.Contains("site-first", StringComparison.Ordinal));
    }

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
