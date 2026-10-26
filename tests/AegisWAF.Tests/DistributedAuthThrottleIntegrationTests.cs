using AegisWAF.Gateway.Auth;
using AegisWAF.Gateway;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Sockets;
using System.Net.Http.Json;

namespace AegisWAF.Tests;

[Collection("gateway")]
public sealed class DistributedAuthThrottleIntegrationTests(GatewayFixture fx)
{
    private static Uri FreeUrl()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return new Uri($"http://127.0.0.1:{port}/");
    }

    [Fact]
    public async Task Shared_postgres_bucket_throttles_failures_across_two_gateway_instances()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = fx.ConnectionString,
            ["Auth:Throttle:Store"] = "postgres",
            ["Auth:Throttle:MaxFailures"] = "2",
            ["Auth:Throttle:MaxEntries"] = "10000",
            ["Auth:Throttle:WindowMinutes"] = "15",
            ["Waf:Deployment:Enforce"] = "true"
        }).Build();
        var firstInstance = new DistributedAuthThrottle(configuration);
        var secondInstance = new DistributedAuthThrottle(configuration);
        var username = "distributed-" + Guid.NewGuid().ToString("N")[..10];
        const string ip = "198.51.100.77";

        try
        {
            Assert.True(await firstInstance.RecordFailureAsync(ip, username, CancellationToken.None));
            Assert.True(await secondInstance.RecordFailureAsync(ip, username, CancellationToken.None));
            Assert.False(await firstInstance.AllowAsync(ip, username, CancellationToken.None));
        }
        finally
        {
            Assert.True(await firstInstance.RecordSuccessAsync(ip, username, CancellationToken.None));
        }
    }

    [Fact]
    public async Task Real_gateway_instances_share_auth_throttle_state_over_postgres()
    {
        var firstUrl = FreeUrl();
        var secondUrl = FreeUrl();
        var username = "distributed-net-" + Guid.NewGuid().ToString("N")[..10];
        var overrides = new Dictionary<string, string?>
        {
            ["Auth:Throttle:Store"] = "postgres",
            ["Auth:Throttle:MaxFailures"] = "2",
            ["Auth:Throttle:MaxEntries"] = "10000",
            ["Auth:Throttle:WindowMinutes"] = "15"
        };
        await using var first = new EmbeddedGateway();
        await using var second = new EmbeddedGateway();
        await first.StartAsync(new EmbeddedGatewayOptions(firstUrl, fx.ConnectionString, null, "http://127.0.0.1:9/", null, overrides));
        await second.StartAsync(new EmbeddedGatewayOptions(secondUrl, fx.ConnectionString, null, "http://127.0.0.1:9/", null, overrides));

        using var firstClient = new HttpClient { BaseAddress = firstUrl };
        using var secondClient = new HttpClient { BaseAddress = secondUrl };
        var before = await fx.CountAudit("auth.throttled", "failure", username);
        using var firstResponse = await firstClient.PostAsJsonAsync("api/auth/login", new { username, password = "wrong-password-123" });
        using var secondResponse = await secondClient.PostAsJsonAsync("api/auth/login", new { username, password = "wrong-password-123" });
        using var thirdResponse = await firstClient.PostAsJsonAsync("api/auth/login", new { username, password = "wrong-password-123" });
        var after = await fx.CountAudit("auth.throttled", "failure", username);

        Assert.Equal(HttpStatusCode.Unauthorized, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, secondResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, thirdResponse.StatusCode);
        Assert.True(after > before, $"Expected shared throttle audit entry; before={before}, after={after}");
    }

    [Fact]
    public async Task Real_gateway_instances_share_site_rate_limit_state_over_postgres()
    {
        var firstUrl = FreeUrl();
        var secondUrl = FreeUrl();
        var upstream = "http://127.0.0.1:9/";
        var overrides = new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "*",
            ["Waf:RateLimit:Store"] = "postgres",
            ["Waf:RateLimit:MaxEntries"] = "10000",
            ["Sites:rate:Enabled"] = "true",
            ["Sites:rate:Hostnames:0"] = "rate.test",
            ["Sites:rate:PathPrefixes:0"] = "/",
            ["Sites:rate:RateLimit:Requests"] = "2",
            ["Sites:rate:RateLimit:WindowSeconds"] = "60",
            ["Sites:rate:RateLimit:GlobalRequests"] = "10000",
            ["Sites:rate:Upstreams:one:Address"] = upstream
        };
        await using var first = new EmbeddedGateway();
        await using var second = new EmbeddedGateway();
        await first.StartAsync(new EmbeddedGatewayOptions(firstUrl, fx.ConnectionString, null, null, null, overrides, PreferConfigurationSites: true));
        await second.StartAsync(new EmbeddedGatewayOptions(secondUrl, fx.ConnectionString, null, null, null, overrides, PreferConfigurationSites: true));

        using var firstClient = new HttpClient { BaseAddress = firstUrl };
        using var secondClient = new HttpClient { BaseAddress = secondUrl };
        using var firstRequest = new HttpRequestMessage(HttpMethod.Get, "/rate-test") { Headers = { Host = "rate.test" } };
        using var secondRequest = new HttpRequestMessage(HttpMethod.Get, "/rate-test") { Headers = { Host = "rate.test" } };
        using var thirdRequest = new HttpRequestMessage(HttpMethod.Get, "/rate-test") { Headers = { Host = "rate.test" } };
        using var firstResponse = await firstClient.SendAsync(firstRequest);
        using var secondResponse = await secondClient.SendAsync(secondRequest);
        using var thirdResponse = await firstClient.SendAsync(thirdRequest);

        Assert.NotEqual(HttpStatusCode.TooManyRequests, firstResponse.StatusCode);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, secondResponse.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, thirdResponse.StatusCode);
    }
}
