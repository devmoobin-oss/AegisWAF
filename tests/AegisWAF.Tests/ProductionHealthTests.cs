using AegisWAF.Gateway;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AegisWAF.Tests;

public class ProductionHealthTests
{
    [Fact]
    public async Task Readiness_is_unhealthy_when_postgres_is_not_configured()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var result = await new PostgresHealthCheck(config).CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }
}
