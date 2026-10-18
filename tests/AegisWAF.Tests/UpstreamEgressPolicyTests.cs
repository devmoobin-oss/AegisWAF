using System.Net;
using AegisWAF.Gateway;

namespace AegisWAF.Tests;

public sealed class UpstreamEgressPolicyTests
{
    [Fact]
    public async Task Private_and_metadata_addresses_are_rejected_even_when_hostname_is_allowed()
    {
        var resolver = new FakeResolver(new Dictionary<string, IPAddress[]>
        {
            ["origin.example.test"] = [IPAddress.Parse("10.0.0.5")]
        });
        var policy = new UpstreamEgressPolicy(new UpstreamEgressOptions(["origin.example.test"], [443], ["https"], AllowPrivateExceptions: false), resolver);

        var result = await policy.CheckAsync(new Uri("https://origin.example.test/api"));

        Assert.False(result.Allowed);
        Assert.Equal("private_target", result.ReasonCode);
    }

    [Fact]
    public async Task Public_allowed_hostname_and_port_are_accepted()
    {
        var resolver = new FakeResolver(new Dictionary<string, IPAddress[]>
        {
            ["origin.example.test"] = [IPAddress.Parse("1.1.1.1")]
        });
        var policy = new UpstreamEgressPolicy(new UpstreamEgressOptions(["origin.example.test"], [443], ["https"], false), resolver);

        var result = await policy.CheckAsync(new Uri("https://origin.example.test/api"));

        Assert.True(result.Allowed);
    }

    [Fact]
    public async Task DNS_answer_change_to_private_is_rejected_on_each_check()
    {
        var resolver = new FakeResolver(new Dictionary<string, IPAddress[]>
        {
            ["origin.example.test"] = [IPAddress.Parse("1.1.1.1")]
        });
        var policy = new UpstreamEgressPolicy(new UpstreamEgressOptions(["origin.example.test"], [443], ["https"], false), resolver);

        Assert.True((await policy.CheckAsync(new Uri("https://origin.example.test/"))).Allowed);
        resolver.Answers["origin.example.test"] = [IPAddress.Parse("127.0.0.1")];
        var rebound = await policy.CheckAsync(new Uri("https://origin.example.test/"));

        Assert.False(rebound.Allowed);
        Assert.Equal("loopback_target", rebound.ReasonCode);
    }

    [Fact]
    public async Task Unapproved_scheme_port_and_hostname_are_rejected()
    {
        var policy = new UpstreamEgressPolicy(new UpstreamEgressOptions(["origin.example.test"], [443], ["https"], false), new FakeResolver([]));

        Assert.Equal("scheme_not_allowed", (await policy.CheckAsync(new Uri("http://origin.example.test/"))).ReasonCode);
        Assert.Equal("port_not_allowed", (await policy.CheckAsync(new Uri("https://origin.example.test:8443/"))).ReasonCode);
        Assert.Equal("host_not_allowed", (await policy.CheckAsync(new Uri("https://other.example.test/"))).ReasonCode);
    }

    [Fact]
    public async Task Connection_boundary_rechecks_dns_answer_before_socket_creation()
    {
        var resolver = new FakeResolver(new Dictionary<string, IPAddress[]>
        {
            ["origin.example.test"] = [IPAddress.Parse("1.1.1.1")]
        });
        var policy = new UpstreamEgressPolicy(new UpstreamEgressOptions(["origin.example.test"], [443], ["https"], false), resolver);

        Assert.True((await policy.CheckConnectionAsync("origin.example.test", 443)).Allowed);
        resolver.Answers["origin.example.test"] = [IPAddress.Parse("169.254.169.254")];

        var rebound = await policy.CheckConnectionAsync("origin.example.test", 443);

        Assert.False(rebound.Allowed);
        Assert.Equal("private_target", rebound.ReasonCode);
    }

    [Fact]
    public async Task Connection_boundary_rejects_disallowed_port_before_dns_lookup()
    {
        var resolver = new FakeResolver([]);
        var policy = new UpstreamEgressPolicy(new UpstreamEgressOptions(["origin.example.test"], [443], ["https"], false), resolver);

        var result = await policy.CheckConnectionAsync("origin.example.test", 8443);

        Assert.False(result.Allowed);
        Assert.Equal("port_not_allowed", result.ReasonCode);
        Assert.Empty(resolver.RequestedHosts);
    }

    [Theory]
    [InlineData("198.18.0.1")]
    [InlineData("0.1.2.3")]
    [InlineData("192.0.2.1")]
    public async Task Connection_boundary_rejects_special_use_ipv4_ranges(string address)
    {
        var policy = new UpstreamEgressPolicy(new UpstreamEgressOptions([address], [443], ["https"], true), new FakeResolver([]));

        var result = await policy.CheckConnectionAsync(address, 443);

        Assert.False(result.Allowed);
        Assert.Equal("special_use_target", result.ReasonCode);
    }

    private sealed class FakeResolver(Dictionary<string, IPAddress[]> answers) : IUpstreamAddressResolver
    {
        public Dictionary<string, IPAddress[]> Answers { get; } = answers;
        public List<string> RequestedHosts { get; } = [];
        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct)
        {
            RequestedHosts.Add(host);
            return Task.FromResult(Answers.TryGetValue(host, out var addresses) ? addresses : Array.Empty<IPAddress>());
        }
    }
}
