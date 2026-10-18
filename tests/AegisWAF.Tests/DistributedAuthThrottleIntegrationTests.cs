using AegisWAF.Gateway.Auth;
using Microsoft.Extensions.Configuration;

namespace AegisWAF.Tests;

[Collection("gateway")]
public sealed class DistributedAuthThrottleIntegrationTests(GatewayFixture fx)
{
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
}
