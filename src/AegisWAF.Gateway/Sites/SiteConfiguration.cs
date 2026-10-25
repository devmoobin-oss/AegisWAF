using System.Net;
using Microsoft.Extensions.Configuration;

namespace AegisWAF.Gateway.Sites;

public static class SiteConfigurationValidator
{
    public static IReadOnlyList<string> Validate(IConfiguration configuration)
    {
        var errors = new List<string>();
        var section = configuration.GetSection("Sites");
        var siteSections = section.GetChildren().ToArray();
        var enforcing = configuration.GetValue("Waf:Deployment:Enforce", false);

        if (siteSections.Length == 0)
        {
            if (enforcing)
                errors.Add("Production requires an explicit Sites configuration");
            return errors;
        }

        var activeHostnames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var configuredPolicies = configuration.GetSection("Waf:Policies").GetChildren()
            .Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var activeSites = 0;
        foreach (var site in siteSections)
        {
            var id = NormalizeId(site.Key);
            var enabled = site.GetValue("Enabled", true);
            if (!enabled) continue;
            activeSites++;

            var wafPolicy = site["WafPolicy"] ?? "default";
            if (enforcing && !string.Equals(wafPolicy, "default", StringComparison.OrdinalIgnoreCase) &&
                !configuredPolicies.Contains(wafPolicy))
                errors.Add($"Site {id} references undefined WafPolicy {wafPolicy}");

            var hostnames = site.GetSection("Hostnames").Get<string[]>() ?? [];
            var normalizedHosts = hostnames.Select(NormalizeHostname).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (hostnames.Any(x => NormalizeHostname(x).Length == 0))
                errors.Add($"Site {id} contains an invalid hostname");
            if (normalizedHosts.Length == 0)
                errors.Add($"Site {id} must define at least one hostname");

            foreach (var hostname in normalizedHosts)
            {
                if (activeHostnames.TryGetValue(hostname, out var other))
                    errors.Add($"Site hostname {hostname} is already assigned to {other}");
                else
                    activeHostnames[hostname] = id;
            }

            var upstreamSections = site.GetSection("Upstreams").GetChildren().ToArray();
            if (upstreamSections.Length == 0)
                errors.Add($"Site {id} must define at least one upstream");

            foreach (var upstream in upstreamSections)
            {
                var address = upstream["Address"];
                if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                    errors.Add($"Site {id} upstream {upstream.Key} must be an absolute HTTP(S) URL");
                if (uri is not null && !string.IsNullOrEmpty(uri.UserInfo))
                    errors.Add($"Site {id} upstream {upstream.Key} must not contain credentials");
                if (enforcing && upstream.GetValue("Weight", 100) is < 1 or > 10_000)
                    errors.Add($"Site {id} upstream {upstream.Key} weight must be between 1 and 10000");
                if (enforcing && uri is not null && IPAddress.TryParse(uri.Host, out var literal) && IsDisallowedLiteral(literal))
                    errors.Add($"Site {id} upstream {upstream.Key} must not target a private, loopback, metadata, multicast, or special-use IP literal");
            }

            if (enforcing)
            {
                var allowedHosts = site.GetSection("Egress:AllowedHosts").Get<string[]>() ?? [];
                var allowedPorts = site.GetSection("Egress:AllowedPorts").Get<int[]>() ?? [];
                var allowedSchemes = site.GetSection("Egress:AllowedSchemes").Get<string[]>() ?? [];
                if (allowedHosts.Length == 0 || allowedPorts.Length == 0 || allowedSchemes.Length == 0 ||
                    allowedSchemes.Any(x => !string.Equals(x, "https", StringComparison.OrdinalIgnoreCase)))
                    errors.Add($"Site {id} must define an explicit HTTPS egress allowlist");
                if (allowedHosts.Any(x => x == "*" || NormalizeHostname(x).Length == 0))
                    errors.Add($"Site {id} egress host entries must be explicit hostnames without wildcards");
                if (allowedPorts.Any(x => x is < 1 or > 65535))
                    errors.Add($"Site {id} egress port entries must be between 1 and 65535");
                if (site.GetValue("Egress:AllowPrivateExceptions", false))
                    errors.Add($"Site {id} must not enable private egress exceptions in Production");
                foreach (var upstream in upstreamSections)
                {
                    if (!Uri.TryCreate(upstream["Address"], UriKind.Absolute, out var uri)) continue;
                    if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                        errors.Add($"Site {id} upstream {upstream.Key} must use HTTPS in Production");
                    if (!allowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase) || !allowedPorts.Contains(uri.Port) ||
                        !allowedSchemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase))
                        errors.Add($"Site {id} egress policy must cover upstream {upstream.Key}");
                }
            }

            var healthPath = site["Health:Path"] ?? "/health";
            var healthInterval = site.GetValue("Health:IntervalSeconds", 10);
            var healthTimeout = site.GetValue("Health:TimeoutSeconds", 3);
            var reactivation = site.GetValue("Health:PassiveReactivationSeconds", 30);
            if (healthPath.Length is < 1 or > 128 || !healthPath.StartsWith('/') ||
                healthPath.Contains('?') || healthPath.Contains('#') || healthPath.Contains('\0'))
                errors.Add($"Site {id} health path must be a relative path without query or fragment");
            if (healthInterval is < 5 or > 300)
                errors.Add($"Site {id} health interval must be between 5 and 300 seconds");
            if (healthTimeout is < 1 or > 30 || healthTimeout >= healthInterval)
                errors.Add($"Site {id} health timeout must be between 1 and 30 seconds and less than the health interval");
            if (reactivation is < 5 or > 3600)
                errors.Add($"Site {id} passive health reactivation must be between 5 and 3600 seconds");

            if (enforcing)
            {
                var rateRequests = site.GetValue("RateLimit:Requests", 300);
                var rateWindow = site.GetValue("RateLimit:WindowSeconds", 60);
                var rateGlobal = site.GetValue("RateLimit:GlobalRequests", 10_000);
                if (rateRequests is < 1 or > 1_000_000)
                    errors.Add($"Site {id} rate requests must be between 1 and 1000000");
                if (rateWindow is < 1 or > 3600)
                    errors.Add($"Site {id} rate window must be between 1 and 3600 seconds");
                if (rateGlobal is < 1 or > 10_000_000)
                    errors.Add($"Site {id} global rate limit must be between 1 and 10000000");
            }
        }

        if (enforcing && activeSites == 0)
            errors.Add("Production requires at least one active Site");

        return errors;
    }

    internal static string NormalizeId(string value) =>
        value.Trim().ToLowerInvariant();

    internal static string NormalizeHostname(string value)
    {
        var hostname = value.Trim().TrimEnd('.').ToLowerInvariant();
        if (hostname.Contains(':') && IPAddress.TryParse(hostname, out _)) return hostname;
        return Uri.CheckHostName(hostname) == UriHostNameType.Unknown ? string.Empty : hostname;
    }

    internal static string NormalizePath(string value)
    {
        var path = value.Trim();
        if (path.Length == 0 || path == "/") return "/";
        if (!path.StartsWith('/')) path = "/" + path;
        return path.TrimEnd('/');
    }

    private static bool IsDisallowedLiteral(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || IPAddress.Any.Equals(address) || IPAddress.IPv6Any.Equals(address)) return true;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            var value = (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]);
            return (value & 0xff000000) == 0x0a000000 ||
                   (value & 0xfff00000) == 0xac100000 ||
                   (value & 0xffff0000) == 0xc0a80000 ||
                   (value & 0xffff0000) == 0xa9fe0000 ||
                   (value & 0xffc00000) == 0x64400000 ||
                   (value & 0xe0000000) == 0xe0000000 ||
                   value < 0x01000000;
        }
        var bytes = address.GetAddressBytes();
        return (bytes.Length >= 4 && bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0d && bytes[3] == 0xb8) ||
               address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || bytes[0] == 0xff || (bytes[0] & 0xfe) == 0xfc;
    }
}

public static class SiteConfigurationLoader
{
    public static SiteConfigurationSnapshot Load(IConfiguration configuration)
    {
        var errors = SiteConfigurationValidator.Validate(configuration);
        if (errors.Count > 0)
            throw new InvalidOperationException("Site configuration rejected:\n - " + string.Join("\n - ", errors));

        var sections = configuration.GetSection("Sites").GetChildren().ToArray();
        if (sections.Length == 0)
            return new SiteConfigurationSnapshot([LoadLegacySite(configuration)], true);

        var sites = sections.Select(BindSite).OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();
        return new SiteConfigurationSnapshot(sites, false);
    }

    private static GatewaySite BindSite(IConfigurationSection section)
    {
        var upstreams = section.GetSection("Upstreams").GetChildren().Select(x =>
        {
            var address = new Uri(x["Address"]!, UriKind.Absolute);
            return new SiteUpstream(x.Key, address, Math.Clamp(x.GetValue("Weight", 100), 1, 10_000));
        }).ToArray();
        var egress = new SiteEgressPolicy(
            section.GetSection("Egress:AllowedHosts").Get<string[]>() ?? upstreams.Select(x => x.Address.Host).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            section.GetSection("Egress:AllowedPorts").Get<int[]>() ?? upstreams.Select(x => x.Address.Port).Distinct().ToArray(),
            section.GetSection("Egress:AllowedSchemes").Get<string[]>() ?? upstreams.Select(x => x.Address.Scheme).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            section.GetValue("Egress:AllowPrivateExceptions", false));
        return new GatewaySite(
            SiteConfigurationValidator.NormalizeId(section.Key),
            section.GetValue("Enabled", true),
            (section.GetSection("Hostnames").Get<string[]>() ?? []).Select(SiteConfigurationValidator.NormalizeHostname).Where(x => x.Length > 0).ToArray(),
            (section.GetSection("PathPrefixes").Get<string[]>() ?? ["/"]).Select(SiteConfigurationValidator.NormalizePath).Distinct(StringComparer.Ordinal).ToArray(),
            upstreams,
            section["WafPolicy"] ?? "default",
            new SiteRateLimitPolicy(
                section.GetValue("RateLimit:Requests", 300),
                section.GetValue("RateLimit:WindowSeconds", 60),
                section.GetValue("RateLimit:GlobalRequests", 10_000)),
            egress,
            new SiteHealthPolicy(
                section["Health:Path"] ?? "/",
                section.GetValue("Health:IntervalSeconds", 10),
                section.GetValue("Health:TimeoutSeconds", 3),
                section.GetValue("Health:PassiveReactivationSeconds", 30)));
    }

    private static GatewaySite LoadLegacySite(IConfiguration configuration)
    {
        var destinations = configuration.GetSection("ReverseProxy:Clusters").GetChildren()
            .SelectMany(cluster => cluster.GetSection("Destinations").GetChildren())
            .Select(destination => new SiteUpstream(destination.Key, new Uri(destination["Address"] ?? "http://localhost:8080/", UriKind.Absolute)))
            .ToArray();
        if (destinations.Length == 0)
            destinations = [new SiteUpstream("app1", new Uri("http://localhost:8080/"))];
        return new GatewaySite("default-site", true, ["localhost", "127.0.0.1"], ["/"], destinations, "default",
            new SiteRateLimitPolicy(), new SiteEgressPolicy(destinations.Select(x => x.Address.Host).ToArray(), destinations.Select(x => x.Address.Port).Distinct().ToArray(), destinations.Select(x => x.Address.Scheme).ToArray()));
    }
}
