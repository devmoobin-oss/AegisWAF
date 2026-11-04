using AegisWAF.Gateway;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AegisWAF.Tests;

public sealed class DataProtectionHealthCheckTests
{
    [Fact]
    public async Task Readiness_health_check_requires_a_working_protect_unprotect_round_trip()
    {
        var provider = new EphemeralDataProtectionProvider();
        var result = await new DataProtectionHealthCheck(provider)
            .CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }
}
