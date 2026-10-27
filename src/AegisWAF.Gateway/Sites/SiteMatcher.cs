namespace AegisWAF.Gateway.Sites;

public sealed record SiteMatchResult(GatewaySite Site, string Hostname, string PathPrefix);

public static class SiteMatcher
{
    public static SiteMatchResult? Match(SiteConfigurationSnapshot snapshot, string? hostHeader, string? requestPath)
    {
        var hostname = NormalizeRequestHost(hostHeader);
        if (hostname.Length == 0) return null;

        var path = NormalizeRequestPath(requestPath);
        SiteMatchResult? best = null;
        foreach (var site in snapshot.Sites)
        {
            if (!site.Enabled || !site.Hostnames.Any(candidate =>
                    string.Equals(SiteConfigurationValidator.NormalizeHostname(candidate), hostname, StringComparison.OrdinalIgnoreCase))) continue;
            foreach (var prefix in site.PathPrefixes
                .Select(SiteConfigurationValidator.NormalizePath)
                .Distinct(StringComparer.Ordinal))
            {
                if (!PathMatches(path, prefix)) continue;
                if (best is null || prefix.Length > best.PathPrefix.Length)
                    best = new SiteMatchResult(site, hostname, prefix);
            }
        }
        return best;
    }

    public static SiteMatchResult? MatchHostname(SiteConfigurationSnapshot snapshot, string? hostHeader)
    {
        var hostname = NormalizeRequestHost(hostHeader);
        if (hostname.Length == 0) return null;

        var site = snapshot.Sites.FirstOrDefault(x =>
            x.Enabled && x.Hostnames.Any(candidate =>
                string.Equals(SiteConfigurationValidator.NormalizeHostname(candidate), hostname, StringComparison.OrdinalIgnoreCase)));
        return site is null ? null : new SiteMatchResult(site, hostname, "");
    }

    private static bool PathMatches(string path, string prefix) =>
        prefix == "/" || path.Equals(prefix, StringComparison.Ordinal) || path.StartsWith(prefix + "/", StringComparison.Ordinal);

    private static string NormalizeRequestHost(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var host = value.Trim();
        if (host.StartsWith("[", StringComparison.Ordinal))
        {
            var closing = host.IndexOf(']');
            if (closing > 0) host = host[1..closing];
        }
        else
        {
            var colon = host.LastIndexOf(':');
            if (colon > 0 && int.TryParse(host[(colon + 1)..], out _))
                host = host[..colon];
        }
        return SiteConfigurationValidator.NormalizeHostname(host);
    }

    private static string NormalizeRequestPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "/";
        var path = value.Split('?', '#')[0];
        return path.StartsWith("/", StringComparison.Ordinal) ? path : "/" + path;
    }
}
