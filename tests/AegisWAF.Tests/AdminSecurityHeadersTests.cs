using Microsoft.AspNetCore.Http;
using AegisWAF.Gateway;

namespace AegisWAF.Tests;

public sealed class AdminSecurityHeadersTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/index.html")]
    [InlineData("/assets/app.js")]
    [InlineData("/api/auth/login")]
    [InlineData("/api/owasp-coverage")]
    public void Applies_hardening_to_admin_owned_paths(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;

        AdminSecurityHeaders.Apply(context);

        Assert.Equal("nosniff", context.Response.Headers["X-Content-Type-Options"]);
        Assert.Equal("no-referrer", context.Response.Headers["Referrer-Policy"]);
        Assert.Equal("DENY", context.Response.Headers["X-Frame-Options"]);
        Assert.Equal("none", context.Response.Headers["X-Permitted-Cross-Domain-Policies"]);
        Assert.Equal("same-origin", context.Response.Headers["Cross-Origin-Opener-Policy"]);
        Assert.Equal("same-origin", context.Response.Headers["Cross-Origin-Resource-Policy"]);
        Assert.Contains("default-src 'self'", context.Response.Headers["Content-Security-Policy"].ToString());
        Assert.Contains("camera=()", context.Response.Headers["Permissions-Policy"].ToString());
        Assert.Equal("no-store", context.Response.Headers.CacheControl);
    }

    [Fact]
    public void Does_not_harden_upstream_paths_or_change_their_headers()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/app/page";

        AdminSecurityHeaders.Apply(context);

        Assert.False(context.Response.Headers.ContainsKey("Content-Security-Policy"));
        Assert.False(AdminSecurityHeaders.IsAdminOwned(context.Request.Path));
    }
}
