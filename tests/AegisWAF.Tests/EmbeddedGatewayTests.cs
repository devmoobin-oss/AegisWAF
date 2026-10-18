using System.Net;
using System.Net.Sockets;
using AegisWAF.Client;
using AegisWAF.Gateway;

namespace AegisWAF.Tests;

/// <summary>The desktop app hosts the gateway in-process; verify that path end to end over a real socket.</summary>
[Collection("gateway")]
public class EmbeddedGatewayTests(GatewayFixture fx)
{
    private static Uri FreeUrl()
    {
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop();
        return new Uri($"http://127.0.0.1:{port}/");
    }

    [Fact]
    public async Task Starts_in_process_serves_the_admin_api_and_stops_cleanly()
    {
        var url = FreeUrl();
        await using var gw = new EmbeddedGateway();
        await gw.StartAsync(new EmbeddedGatewayOptions(url, fx.ConnectionString, null, "http://127.0.0.1:9/"));
        Assert.True(gw.Running);

        using var api = new ApiClient(url);
        Assert.True(await api.IsHealthyAsync());
        var s = await api.LoginAsync(GatewayFixture.AdminUser, GatewayFixture.AdminPassword); // user already bootstrapped in the shared DB
        Assert.Equal("SUPER_ADMIN", s.Role);
        Assert.NotNull(await api.GetOverviewAsync());

        await gw.StopAsync();
        Assert.False(gw.Running);
        Assert.False(await api.IsHealthyAsync());
    }

    [Fact]
    public async Task Bad_database_credentials_fail_startup_with_an_exception_and_leave_nothing_running()
    {
        var url = FreeUrl();
        await using var gw = new EmbeddedGateway();
        await Assert.ThrowsAnyAsync<Exception>(() => gw.StartAsync(new EmbeddedGatewayOptions(url,
            "Host=localhost;Port=5432;Database=aegiswaf_does_not_exist;Username=postgres;Password=wrong-password;Timeout=3", null, null)));
        Assert.False(gw.Running);
        using var api = new ApiClient(url);
        Assert.False(await api.IsHealthyAsync());
    }

    [Fact]
    public async Task Port_already_in_use_fails_startup()
    {
        var url = FreeUrl();
        using var blocker = new TcpListener(IPAddress.Loopback, url.Port); blocker.Start();
        await using var gw = new EmbeddedGateway();
        await Assert.ThrowsAnyAsync<Exception>(() => gw.StartAsync(new EmbeddedGatewayOptions(url, fx.ConnectionString, null, null)));
        Assert.False(gw.Running);
    }
}
