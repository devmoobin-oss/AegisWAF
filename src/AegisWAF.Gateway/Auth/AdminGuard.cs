using System.Net;

namespace AegisWAF.Gateway.Auth;

/// <summary>
/// Role levels kept only as documentation on each endpoint; the login system that used to enforce them has been
/// removed, so <see cref="AdminGuard.Require"/> no longer checks anything against these values.
/// </summary>
internal static class Roles
{
    public const int Viewer = 1, Analyst = 2, Operator = 3, SecurityAdmin = 4, Super = 5;
}

internal static class AdminGuard
{
    public static string? Actor(this HttpContext ctx) => null;
    public static string ClientIp(this HttpContext ctx) => ClientAddress.Resolve(ctx, ctx.RequestServices.GetRequiredService<IConfiguration>());

    /// <summary>Null remote address only occurs for in-memory/unix-socket transports, never for a network client.</summary>
    public static bool IsLocalClient(HttpContext ctx) => ctx.Connection.RemoteIpAddress is null || IPAddress.IsLoopback(ctx.Connection.RemoteIpAddress);

    /// <summary>No-op kept so existing endpoint declarations compile unchanged; there is no session or role to require.</summary>
    public static RouteHandlerBuilder Require(this RouteHandlerBuilder b, int minLevel, bool allowMustChange = false) => b;

    /// <summary>
    /// Network perimeter only: administration is restricted to the local host unless Admin:AllowRemote is set, in
    /// which case it must be HTTPS. There is no login — every request that passes this check is treated as authorized.
    /// </summary>
    public static async Task Invoke(HttpContext ctx, Func<Task> next)
    {
        if (!ctx.Request.Path.StartsWithSegments("/api")) { await next(); return; }
        var cfg = ctx.RequestServices.GetRequiredService<IConfiguration>();
        var remoteAdmin = cfg.GetValue("Admin:AllowRemote", false);
        if (!remoteAdmin && !IsLocalClient(ctx)) { await Deny(ctx, 403, "Administration is restricted to the local host"); return; }
        if (remoteAdmin && cfg.GetValue("Admin:RequireHttps", true) && !AdminPerimeterPolicy.IsSecureRequest(ctx, cfg))
        { await Deny(ctx, 400, "Remote administration requires HTTPS"); return; }
        ctx.Response.Headers.CacheControl = "no-store";
        await next();
    }

    private static Task Deny(HttpContext ctx, int status, string message)
    {
        ctx.Response.StatusCode = status;
        return ctx.Response.WriteAsJsonAsync(new { error = message });
    }
}
