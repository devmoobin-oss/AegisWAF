using System.Net;

namespace AegisWAF.Gateway;

/// <summary>Shared control-plane boundary rules for trusted proxies and optional listener isolation.</summary>
internal static class AdminPerimeterPolicy
{
    public static bool IsAdminOwned(PathString path) =>
        path == "/" || path == "/index.html" || path.StartsWithSegments("/assets") ||
        path.StartsWithSegments("/api") || path.StartsWithSegments("/health") || path.StartsWithSegments("/ready");

    public static bool IsSecureRequest(HttpContext context, IConfiguration configuration) =>
        string.Equals(EffectiveScheme(context, configuration), Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

    public static string EffectiveScheme(HttpContext context, IConfiguration configuration)
    {
        if (ClientAddress.IsTrustedPeer(context, configuration))
        {
            var forwarded = FirstHeaderValue(context.Request.Headers["X-Forwarded-Proto"]);
            if (string.Equals(forwarded, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(forwarded, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                return forwarded.ToLowerInvariant();
        }
        return context.Request.Scheme;
    }

    public static HostString EffectiveHost(HttpContext context, IConfiguration configuration)
    {
        if (ClientAddress.IsTrustedPeer(context, configuration))
        {
            var forwarded = FirstHeaderValue(context.Request.Headers["X-Forwarded-Host"]);
            if (TryParseHost(forwarded, out var host)) return host;
        }
        return context.Request.Host;
    }

    public static bool OriginMatchesRequest(string origin, HttpContext context, IConfiguration configuration)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, EffectiveScheme(context, configuration), StringComparison.OrdinalIgnoreCase))
            return false;

        var expected = EffectiveHost(context, configuration);
        if (!string.Equals(uri.Host, expected.Host, StringComparison.OrdinalIgnoreCase)) return false;
        var expectedPort = expected.Port ?? DefaultPort(uri.Scheme);
        var originPort = uri.IsDefaultPort ? DefaultPort(uri.Scheme) : uri.Port;
        return originPort == expectedPort;
    }

    public static string? ValidateRequest(HttpContext context, IConfiguration configuration)
    {
        var adminPort = ListenerPort(configuration, "Admin:ListenerPort");
        var gatewayPort = ListenerPort(configuration, "Gateway:ListenerPort");
        var isAdminPath = IsAdminOwned(context.Request.Path);
        var localPort = context.Connection.LocalPort;

        if (adminPort > 0 && isAdminPath && localPort != adminPort)
            return "Administration is restricted to the admin listener";
        if (gatewayPort > 0 && !isAdminPath && localPort != gatewayPort)
            return "Protected application traffic is restricted to the gateway listener";
        return null;
    }

    public static IReadOnlyList<string> ValidateConfiguration(IConfiguration configuration)
    {
        if (!configuration.GetValue("Waf:Deployment:Enforce", false)) return [];

        var errors = new List<string>();
        var adminPort = ListenerPort(configuration, "Admin:ListenerPort");
        var gatewayPort = ListenerPort(configuration, "Gateway:ListenerPort");
        if (adminPort is < 1 or > 65535 || gatewayPort is < 1 or > 65535)
            errors.Add("Production listener isolation requires both Admin:ListenerPort and Gateway:ListenerPort between 1 and 65535");
        else if (adminPort == gatewayPort)
            errors.Add("Admin:ListenerPort and Gateway:ListenerPort must be different");
        return errors;
    }

    private static int ListenerPort(IConfiguration configuration, string key) => configuration.GetValue(key, 0);

    private static string FirstHeaderValue(string? value) =>
        (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty;

    private static bool TryParseHost(string value, out HostString host)
    {
        host = default;
        if (string.IsNullOrWhiteSpace(value) || value.Contains("//", StringComparison.Ordinal) ||
            !Uri.TryCreate("https://" + value, UriKind.Absolute, out var uri) ||
            !string.IsNullOrEmpty(uri.UserInfo) || uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            return false;
        host = uri.IsDefaultPort ? new HostString(uri.Host) : new HostString(uri.Host, uri.Port);
        return !string.IsNullOrWhiteSpace(host.Host);
    }

    private static int DefaultPort(string scheme) => string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ? 443 : 80;
}
