using System.Net;
using AegisWAF.Gateway;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace AegisWAF.Tests;

public sealed class AdminPerimeterTests
{
    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(x => x.Key, x => (string?)x.Value)).Build();

    [Fact]
    public void Forwarded_https_is_secure_only_when_direct_peer_is_trusted()
    {
        var cfg = Config(("Security:TrustedProxies:0", "10.0.0.0/8"));
        var ctx = new DefaultHttpContext();
        ctx.Request.Scheme = "http";
        ctx.Connection.RemoteIpAddress = IPAddress.Parse("10.1.2.3");
        ctx.Request.Headers["X-Forwarded-Proto"] = "https";
        Assert.True(AdminPerimeterPolicy.IsSecureRequest(ctx, cfg));

        ctx.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.10");
        Assert.False(AdminPerimeterPolicy.IsSecureRequest(ctx, cfg));
    }

    [Fact]
    public void Forwarded_host_is_used_for_origin_only_from_a_trusted_proxy()
    {
        var cfg = Config(("Security:TrustedProxies:0", "10.0.0.0/8"));
        var ctx = new DefaultHttpContext();
        ctx.Request.Host = new HostString("internal:8080");
        ctx.Connection.RemoteIpAddress = IPAddress.Parse("10.1.2.3");
        ctx.Request.Headers["X-Forwarded-Host"] = "admin.example.test";
        ctx.Request.Headers["X-Forwarded-Proto"] = "https";

        Assert.True(AdminPerimeterPolicy.OriginMatchesRequest("https://admin.example.test", ctx, cfg));

        ctx.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.10");
        Assert.False(AdminPerimeterPolicy.OriginMatchesRequest("https://admin.example.test", ctx, cfg));
    }

    [Fact]
    public void Dual_listener_mode_isolates_admin_and_gateway_paths()
    {
        var cfg = Config(("Admin:ListenerPort", "8443"), ("Gateway:ListenerPort", "8080"));
        var admin = new DefaultHttpContext();
        admin.Connection.LocalPort = 8443;
        var gateway = new DefaultHttpContext();
        gateway.Connection.LocalPort = 8080;

        admin.Request.Path = "/api/auth/session";
        gateway.Request.Path = "/api/auth/session";
        Assert.Null(AdminPerimeterPolicy.ValidateRequest(admin, cfg));
        Assert.NotNull(AdminPerimeterPolicy.ValidateRequest(gateway, cfg));

        admin.Request.Path = "/protected";
        gateway.Request.Path = "/protected";
        Assert.NotNull(AdminPerimeterPolicy.ValidateRequest(admin, cfg));
        Assert.Null(AdminPerimeterPolicy.ValidateRequest(gateway, cfg));
    }

    [Fact]
    public void Zero_listener_ports_preserve_desktop_single_listener_mode()
    {
        var cfg = Config();
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/api/overview";
        Assert.Null(AdminPerimeterPolicy.ValidateRequest(ctx, cfg));
        ctx.Request.Path = "/protected";
        Assert.Null(AdminPerimeterPolicy.ValidateRequest(ctx, cfg));
    }

    [Fact]
    public void Production_requires_two_distinct_listener_ports_when_enforced()
    {
        var onePort = Config(("Waf:Deployment:Enforce", "true"), ("Admin:ListenerPort", "8443"));
        Assert.Contains(AdminPerimeterPolicy.ValidateConfiguration(onePort), x => x.Contains("both", StringComparison.OrdinalIgnoreCase));

        var samePort = Config(("Waf:Deployment:Enforce", "true"), ("Admin:ListenerPort", "8443"), ("Gateway:ListenerPort", "8443"));
        Assert.Contains(AdminPerimeterPolicy.ValidateConfiguration(samePort), x => x.Contains("different", StringComparison.OrdinalIgnoreCase));
    }
}
