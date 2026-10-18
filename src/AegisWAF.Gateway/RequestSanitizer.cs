namespace AegisWAF.Gateway;

internal static class RequestSanitizer
{
    public static string? Validate(HttpRequest request, IConfiguration cfg)
    {
        var method = string.IsNullOrWhiteSpace(request.Method) ? HttpMethods.Get : request.Method;
        if (!cfg.GetValue("Waf:AllowUnsafeMethods", false) && (HttpMethods.IsTrace(method) || HttpMethods.IsConnect(method)))
            return "Unsafe HTTP method is disabled";
        var allowed = cfg.GetSection("Waf:AllowedMethods").Get<string[]>() ?? ["GET", "HEAD", "POST", "PUT", "PATCH", "DELETE", "OPTIONS"];
        if (!allowed.Contains(method, StringComparer.OrdinalIgnoreCase)) return "HTTP method is not allowed";
        var transferEncoding = request.Headers.TransferEncoding.ToString();
        var contentLength = request.Headers["Content-Length"].ToString();
        if (!string.IsNullOrWhiteSpace(transferEncoding) && !string.IsNullOrWhiteSpace(contentLength))
            return "Ambiguous message framing";
        if (contentLength.Contains(',', StringComparison.Ordinal)) return "Duplicate Content-Length";
        if (!string.IsNullOrWhiteSpace(transferEncoding) &&
            !string.Equals(transferEncoding.Trim(), "chunked", StringComparison.OrdinalIgnoreCase))
            return "Unsupported Transfer-Encoding";
        var contentEncoding = request.Headers.ContentEncoding.ToString();
        if (!string.IsNullOrWhiteSpace(contentEncoding) &&
            !string.Equals(contentEncoding.Trim(), "identity", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(contentEncoding.Trim(), "gzip", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(contentEncoding.Trim(), "deflate", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(contentEncoding.Trim(), "br", StringComparison.OrdinalIgnoreCase))
            return "Unsupported request content encoding";
        var upgrade = request.Headers.Upgrade.ToString();
        if (!string.IsNullOrWhiteSpace(upgrade) && !cfg.GetValue("Waf:AllowProtocolUpgrade", false))
            return "Protocol upgrade is disabled";
        return null;
    }

    public static void RemoveUntrustedForwardedHeaders(HttpContext context, IConfiguration cfg)
    {
        var peer = context.Connection.RemoteIpAddress;
        if (peer is not null && ClientAddress.IsTrusted(peer, cfg)) return;
        foreach (var name in new[] { "X-Forwarded-For", "X-Forwarded-Host", "X-Forwarded-Proto", "Forwarded" })
            context.Request.Headers.Remove(name);
    }
}
