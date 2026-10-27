using AegisWAF.Gateway;
using AegisWAF.Gateway.Sites;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AegisWAF.Tests;

public sealed class SiteRuntimeListenerHealthCheckTests
{
    [Fact]
    public async Task Reconnecting_listener_is_degraded_without_leaking_database_details()
    {
        var status = new SiteRuntimeListenerStatus();
        status.MarkConnecting();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Waf:Sites:UseDatabase"] = "true",
            ["ConnectionStrings:Postgres"] = "Host=internal-db;Password=secret"
        }).Build();

        var result = await new SiteRuntimeListenerHealthCheck(status, configuration)
            .CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.DoesNotContain("internal-db", result.Description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Disabled_database_listener_is_healthy_for_readiness()
    {
        var status = new SiteRuntimeListenerStatus();
        status.MarkDisabled();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Waf:Sites:UseDatabase"] = "false"
        }).Build();

        var result = await new SiteRuntimeListenerHealthCheck(status, configuration)
            .CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task Connected_listener_with_a_failed_snapshot_reload_is_degraded()
    {
        var status = new SiteRuntimeListenerStatus();
        status.MarkConnecting();
        status.MarkConnected();
        status.MarkReloadFailure(new InvalidDataException("invalid persisted snapshot"));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Waf:Sites:UseDatabase"] = "true",
            ["ConnectionStrings:Postgres"] = "Host=internal-db;Password=secret"
        }).Build();

        var result = await new SiteRuntimeListenerHealthCheck(status, configuration)
            .CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("snapshot reload", result.Description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("internal-db", result.Description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Successful_snapshot_reload_restores_healthy_listener_readiness()
    {
        var status = new SiteRuntimeListenerStatus();
        status.MarkConnecting();
        status.MarkConnected();
        status.MarkReloadFailure(new InvalidDataException("invalid persisted snapshot"));
        status.MarkReloaded();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Waf:Sites:UseDatabase"] = "true",
            ["ConnectionStrings:Postgres"] = "Host=internal-db;Password=secret"
        }).Build();

        var result = await new SiteRuntimeListenerHealthCheck(status, configuration)
            .CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }
}
