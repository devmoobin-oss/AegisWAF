using System.Net;
using AegisWAF.Gateway;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace AegisWAF.Tests;

public class ProductionSecurityTests
{
    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(x => x.Key, x => (string?)x.Value)).Build();

    [Fact]
    public void Forwarded_client_is_accepted_only_from_an_explicitly_trusted_proxy()
    {
        var cfg = Config(("Security:TrustedProxies:0", "10.0.0.0/8"));
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = IPAddress.Parse("10.1.2.3");
        ctx.Request.Headers["X-Forwarded-For"] = "198.51.100.9, 10.1.2.3";
        Assert.Equal("198.51.100.9", ClientAddress.Resolve(ctx, cfg));

        ctx.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.1");
        Assert.Equal("192.0.2.1", ClientAddress.Resolve(ctx, cfg));
    }

    [Fact]
    public void Cidr_supports_ipv4_and_ipv6_without_dns_or_network_access()
    {
        Assert.True(Cidr.Contains("192.0.2.0/24", IPAddress.Parse("192.0.2.44")));
        Assert.False(Cidr.Contains("192.0.2.0/24", IPAddress.Parse("192.0.3.44")));
        Assert.True(Cidr.Contains("2001:db8::/32", IPAddress.Parse("2001:db8::10")));
    }

    [Fact]
    public void Rolling_limiter_never_allows_more_than_the_configured_window()
    {
        var limiter = new SlidingRateLimiter(Config(("Waf:RateLimit:Requests", "2"), ("Waf:RateLimit:WindowSeconds", "60")));
        Assert.True(limiter.Allow("198.51.100.1"));
        Assert.True(limiter.Allow("198.51.100.1"));
        Assert.False(limiter.Allow("198.51.100.1"));
        Assert.True(limiter.Allow("198.51.100.2"));
    }

    [Fact]
    public void Global_ceiling_limits_distributed_clients_when_enabled()
    {
        var limiter = new SlidingRateLimiter(Config(
            ("Waf:RateLimit:Requests", "10"),
            ("Waf:RateLimit:GlobalRequests", "2"),
            ("Waf:RateLimit:WindowSeconds", "60")));

        Assert.True(limiter.Allow("198.51.100.1"));
        Assert.True(limiter.Allow("198.51.100.2"));
        Assert.False(limiter.Allow("198.51.100.3"));
    }

    [Fact]
    public void Full_bucket_table_fails_closed_for_new_clients_but_preserves_existing_clients()
    {
        var limiter = new SlidingRateLimiter(Config(
            ("Waf:RateLimit:Requests", "10"),
            ("Waf:RateLimit:MaxEntries", "100"),
            ("Waf:RateLimit:WindowSeconds", "60")));

        for (var i = 0; i < 100; i++) Assert.True(limiter.Allow($"198.51.100.{i % 250}"));
        Assert.True(limiter.Allow("198.51.100.0"));
        Assert.False(limiter.Allow("203.0.113.250"));
    }

    [Theory]
    [InlineData(99, 0, 1, true)]
    [InlineData(100, 0, 1, false)]
    [InlineData(100, 1, 1, true)]
    [InlineData(100, 0, 2, false)]
    public void Distributed_bucket_capacity_allows_existing_keys_but_rejects_new_cardinality(
        long totalRows, long existingCandidateRows, int candidateKeyCount, bool expected)
    {
        Assert.Equal(expected, RateLimitCapacityPolicy.Allows(totalRows, existingCandidateRows, candidateKeyCount, 100));
    }
}
