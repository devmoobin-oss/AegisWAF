using System.Net;

namespace AegisWAF.Tests;

public sealed class IpPolicyUnitTests
{
    [Fact]
    public void Ip_policy_normalizes_ipv4_cidr_network_and_matches_members()
    {
        var policies = new global::IpAccessPolicy();
        policies.Upsert("192.0.2.44/24", "block", "network");

        Assert.Equal("block", policies.Check("192.0.2.10"));
    }

    [Fact]
    public void Ip_policy_matches_ipv6_cidr_without_crossing_the_prefix()
    {
        var policies = new global::IpAccessPolicy();
        policies.Upsert("2001:db8:abcd:1234::/64", "allow", "trusted subnet");

        Assert.Equal("allow", policies.Check(IPAddress.Parse("2001:db8:abcd:1234::99").ToString()));
        Assert.Null(policies.Check(IPAddress.Parse("2001:db8:abcd:1235::1").ToString()));
    }

    [Fact]
    public void Exact_policy_wins_over_a_broad_cidr_policy()
    {
        var policies = new global::IpAccessPolicy();
        policies.Upsert("198.51.100.0/24", "allow", "network");
        policies.Upsert("198.51.100.44", "block", "host exception");

        Assert.Equal("block", policies.Check("198.51.100.44"));
        Assert.Equal("allow", policies.Check("198.51.100.45"));
    }

    [Fact]
    public void Most_specific_cidr_policy_wins_over_a_broad_cidr_policy()
    {
        var policies = new global::IpAccessPolicy();
        policies.Upsert("198.51.0.0/16", "allow", "broad network");
        policies.Upsert("198.51.100.0/24", "block", "restricted subnet");

        Assert.Equal("block", policies.Check("198.51.100.20"));
        Assert.Equal("allow", policies.Check("198.51.101.20"));
    }

}

[Collection("gateway")]
public sealed class IpPolicyApiTests(GatewayFixture fx)
{
    [Fact]
    public async Task Api_accepts_and_deletes_a_cidr_policy()
    {
        var api = await fx.LoginAdmin();
        try
        {
            const string submitted = "203.0.113.44/24";
            const string normalized = "203.0.113.0/24";
            var save = await api.Send(HttpMethod.Put, "/api/ip-policies", new { ip = submitted, action = "block", reason = "cidr test" });
            Assert.Equal(HttpStatusCode.NoContent, save.StatusCode);
            Assert.Contains((await api.Json(await api.Get("/api/ip-policies"))).EnumerateArray(), p => p.GetProperty("ip").GetString() == normalized);

            var delete = await api.Send(HttpMethod.Delete, "/api/ip-policies?ip=" + Uri.EscapeDataString(normalized));
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
            Assert.DoesNotContain((await api.Json(await api.Get("/api/ip-policies"))).EnumerateArray(), p => p.GetProperty("ip").GetString() == normalized);
        }
        finally
        {
            // The query endpoint is idempotent and also cleans up if a later assertion fails.
            var cleanup = await fx.LoginAdmin();
            await cleanup.Send(HttpMethod.Delete, "/api/ip-policies?ip=" + Uri.EscapeDataString("203.0.113.0/24"));
        }
    }
}
