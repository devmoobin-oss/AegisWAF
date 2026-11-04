namespace AegisWAF.Client;

/// <summary>Provides immediate, Thai-language guidance for common Multi-site form mistakes.</summary>
public static class SiteFormInputValidator
{
    private static readonly System.Text.RegularExpressions.Regex SiteIdPattern =
        new("^[a-z0-9][a-z0-9_-]{0,63}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    public static string? Validate(string? siteId, IReadOnlyList<string>? hostnames, string? pathPrefix, string? upstream)
    {
        if (string.IsNullOrWhiteSpace(siteId)) return "กรุณากรอกรหัส Site เช่น affiliateiq";
        if (!SiteIdPattern.IsMatch(siteId.Trim().ToLowerInvariant()))
            return "รหัส Site ต้องมี 1-64 ตัว และใช้ตัวอักษรภาษาอังกฤษ ตัวเลข ขีดกลาง หรือขีดล่างเท่านั้น";
        if (hostnames is null || hostnames.Count == 0) return "กรุณากรอกชื่อ Hostname อย่างน้อย 1 รายการ";
        if (hostnames.Any(ContainsPort)) return "Hostname ไม่ต้องใส่ port เช่นใช้ affiliateiq.localhost แทน localhost:4004";
        if (string.IsNullOrWhiteSpace(pathPrefix)) return "กรุณากรอกเส้นทาง URL เช่น / หรือ /api";
        return ValidateUpstreamUrl(upstream);
    }

    public static string? ValidateUpstreamUrl(string? upstream)
    {
        if (!Uri.TryCreate(upstream?.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(uri.UserInfo))
            return "Upstream ต้องเริ่มด้วย http:// หรือ https:// เช่น http://localhost:4000/ และห้ามมี username/password";
        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            return "Upstream ต้องไม่มี query หรือ fragment; ให้ใช้ base path เท่านั้น";
        return null;
    }

    private static bool ContainsPort(string hostname)
    {
        var value = hostname.Trim();
        return value.Contains(':') && !(value.StartsWith("[", StringComparison.Ordinal) && value.EndsWith("]", StringComparison.Ordinal));
    }
}
