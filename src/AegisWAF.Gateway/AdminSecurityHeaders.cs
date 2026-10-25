using Microsoft.AspNetCore.Http;

namespace AegisWAF.Gateway;

public static class AdminSecurityHeaders
{
    public static bool IsAdminOwned(PathString path) => AdminPerimeterPolicy.IsAdminOwned(path);

    public static void Apply(HttpContext context, IConfiguration? configuration = null)
    {
        if (!IsAdminOwned(context.Request.Path)) return;
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["X-Permitted-Cross-Domain-Policies"] = "none";
        context.Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";
        context.Response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
        context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
        var secure = configuration is not null ? AdminPerimeterPolicy.IsSecureRequest(context, configuration) : context.Request.IsHttps;
        if (secure) context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
        context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'";
        context.Response.Headers.CacheControl = "no-store";
    }
}
