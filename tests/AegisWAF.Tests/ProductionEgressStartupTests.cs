using System.Net;
using AegisWAF.Gateway;
using AegisWAF.Gateway.Sites;

namespace AegisWAF.Tests;

public sealed class ProductionEgressStartupTests
{
    [Fact]
    public async Task Startup_preflight_rejects_hostname_resolving_to_private_target()
    {
        var site = new GatewaySite("site-a", true, ["app.example.com"], ["/"],
            [new SiteUpstream("primary", new Uri("https://origin.example.test/"))], "default",
            new SiteRateLimitPolicy(), new SiteEgressPolicy(["origin.example.test"], [443], ["https"]));
        var resolver = new FakeResolver(new Dictionary<string, IPAddress[]>
        {
            ["origin.example.test"] = [IPAddress.Parse("10.0.0.10")]
        });
        var policy = new UpstreamEgressPolicy(UpstreamEgressOptions.From(site.Egress), resolver);
        var validator = new ProductionEgressStartupValidator(
            new SiteConfigurationSnapshot([site], false), policy);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => validator.StartAsync(CancellationToken.None));

        Assert.Contains("private_target", error.Message, StringComparison.Ordinal);
    }

    private sealed class FakeResolver(Dictionary<string, IPAddress[]> answers) : IUpstreamAddressResolver
    {
        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct) =>
            Task.FromResult(answers.TryGetValue(host, out var addresses) ? addresses : Array.Empty<IPAddress>());
    }
}
