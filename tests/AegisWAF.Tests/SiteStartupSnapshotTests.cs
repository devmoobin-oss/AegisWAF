using AegisWAF.Gateway;
using AegisWAF.Gateway.Sites;
using Microsoft.Extensions.DependencyInjection;

namespace AegisWAF.Tests;

public sealed class SiteStartupSnapshotTests
{
    [Fact]
    public void Builder_and_registry_share_one_immutable_site_snapshot_instance()
    {
        var builder = GatewayApp.CreateBuilder([], overrides: new Dictionary<string, string?>
        {
            ["Waf:Sites:UseDatabase"] = "false",
            ["Sites:shop:Enabled"] = "true",
            ["Sites:shop:Hostnames:0"] = "shop.example.test",
            ["Sites:shop:PathPrefixes:0"] = "/",
            ["Sites:shop:Upstreams:0:Address"] = "https://origin.example.test/"
        });

        GatewayApp.AddServices(builder);
        using var app = builder.Build();
        var services = app.Services;
        var snapshot = services.GetRequiredService<SiteConfigurationSnapshot>();

        Assert.Same(snapshot, services.GetRequiredService<SiteRegistry>().Current);
    }
}
