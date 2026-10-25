using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace AegisWAF.Gateway.Auth;

internal static class AdminGuard
{
    public const string CookieName = "aegis_session";
    private const string SessionKey = "aegis.session";

    public static SessionInfo? Session(this HttpContext ctx) => ctx.Items.TryGetValue(SessionKey, out var s) ? s as SessionInfo : null;
    public static string? Actor(this HttpContext ctx) => ctx.Session()?.Username;
    public static string ClientIp(this HttpContext ctx) => ClientAddress.Resolve(ctx, ctx.RequestServices.GetRequiredService<IConfiguration>());

    /// <summary>Null remote address only occurs for in-memory/unix-socket transports, never for a network client.</summary>
    public static bool IsLocalClient(HttpContext ctx) => ctx.Connection.RemoteIpAddress is null || IPAddress.IsLoopback(ctx.Connection.RemoteIpAddress);

    public static RouteHandlerBuilder Require(this RouteHandlerBuilder b, int minLevel, bool allowMustChange = false) => b.WithMetadata(new AdminAccess(minLevel, allowMustChange));

    /// <summary>
    /// Gatekeeper for every /api request: network boundary, deny-by-default routing, session, role level, must-change-password and CSRF.
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

        var endpoint = ctx.GetEndpoint();
        if (endpoint is null) { await Deny(ctx, 404, "Not found"); return; }
        var access = endpoint.Metadata.GetMetadata<AdminAccess>();
        // Routes without explicit access metadata (e.g. the proxy catch-all) must never be reachable under /api.
        if (access is null) { await Deny(ctx, 403, "Forbidden"); return; }
        if (access.MinLevel == 0) { await next(); return; }

        var store = ctx.RequestServices.GetRequiredService<AuthStore>();
        SessionInfo? session = null;
        if (ctx.Request.Cookies.TryGetValue(CookieName, out var token) && !string.IsNullOrEmpty(token))
        {
            try { session = await store.ValidateSessionAsync(token, ctx.RequestAborted); }
            catch (Exception ex) when (ex is Npgsql.NpgsqlException or InvalidOperationException) { await Deny(ctx, 503, "Authentication service unavailable"); return; }
        }
        if (session is null) { await Deny(ctx, 401, "Unauthorized"); return; }
        ctx.Items[SessionKey] = session;

        if (session.MustChange && !access.AllowMustChange) { await Deny(ctx, 403, "Password change required"); return; }
        if (session.Level < access.MinLevel)
        {
            await SafeAudit(store, session.Username, ctx, "authz.denied", $"{ctx.Request.Method} {ctx.Request.Path}", "denied");
            await Deny(ctx, 403, "Insufficient role"); return;
        }
        if (!HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method) && !HttpMethods.IsOptions(ctx.Request.Method))
        {
            var origin = ctx.Request.Headers.Origin.ToString();
            if (!string.IsNullOrWhiteSpace(origin) && !AdminPerimeterPolicy.OriginMatchesRequest(origin, ctx, cfg))
            { await Deny(ctx, 403, "Origin validation failed"); return; }
            var provided = Encoding.UTF8.GetBytes(ctx.Request.Headers["X-CSRF-Token"].ToString());
            var expected = Encoding.UTF8.GetBytes(session.CsrfToken);
            if (provided.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(provided, expected)) { await Deny(ctx, 403, "CSRF validation failed"); return; }
        }
        await next();
    }

    private static async Task SafeAudit(AuthStore store, string? actor, HttpContext ctx, string action, string target, string outcome)
    {
        try { await store.AuditAsync(actor, ctx.ClientIp(), action, target.Length > 200 ? target[..200] : target, outcome, ctx.RequestAborted); }
        catch (Exception) { /* audit failure must not turn a denial into an allow */ }
    }

    private static Task Deny(HttpContext ctx, int status, string message)
    {
        ctx.Response.StatusCode = status;
        return ctx.Response.WriteAsJsonAsync(new { error = message });
    }
}
