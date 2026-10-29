using System.Net;
using Microsoft.Extensions.Configuration;
using System.Globalization;

namespace AegisWAF.Gateway.Sites;

public static class SiteConfigurationValidator
{
    private static readonly IdnMapping HostnameIdn = new() { UseStd3AsciiRules = true };
    public const int DefaultMaxSites = 10_000;
    public const int MaximumAllowedSites = 100_000;
    public const int DefaultMaxGeneratedRoutes = 100_000;
    public const int MaximumAllowedGeneratedRoutes = 1_000_000;
    public const int DefaultMaxGeneratedDestinations = 100_000;
    public const int MaximumAllowedGeneratedDestinations = 1_000_000;
    private const int MaxHostnamesPerSite = 32;
    private const int MaxPathPrefixesPerSite = 64;
    private const int MaxUpstreamsPerSite = 32;
    private const int MaxEgressHostsPerSite = 64;
    private const int MaxEgressPortsPerSite = 32;
    private const int MaxEgressSchemesPerSite = 4;

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

        var maxSites = ResolveMaxSites(configuration);
        var maxGeneratedRoutes = ResolveMaxGeneratedRoutes(configuration);
        var maxGeneratedDestinations = ResolveMaxGeneratedDestinations(configuration);
        long generatedRoutes = 0;
        long generatedDestinations = 0;
        if (siteSections.Length > maxSites)
            errors.Add($"Configuration may contain at most {maxSites} Sites");

        var activeHostnames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var configuredPolicies = configuration.GetSection("Waf:Policies").GetChildren()
            .Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var activeSites = 0;
        foreach (var site in siteSections)
        {
            var id = NormalizeId(site.Key);
            if (!RegexPatterns.SiteId.IsMatch(id))
                errors.Add($"Site {site.Key} has an invalid ID");
            var enabled = site.GetValue("Enabled", true);
            if (!enabled) continue;
            activeSites++;

            var wafPolicy = site["WafPolicy"] ?? "default";
            if (enforcing && !string.Equals(wafPolicy, "default", StringComparison.OrdinalIgnoreCase) &&
                !configuredPolicies.Contains(wafPolicy))
                errors.Add($"Site {id} references undefined WafPolicy {wafPolicy}");

            var hostnames = site.GetSection("Hostnames").Get<string[]>() ?? [];
            if (hostnames.Length > MaxHostnamesPerSite)
                errors.Add($"Site {id} may contain at most {MaxHostnamesPerSite} hostnames");
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
            else if (upstreamSections.Length > MaxUpstreamsPerSite)
                errors.Add($"Site {id} may contain at most {MaxUpstreamsPerSite} upstreams");

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

            var pathPrefixes = site.GetSection("PathPrefixes").Get<string[]>() ?? ["/"];
            if (pathPrefixes.Length > MaxPathPrefixesPerSite)
                errors.Add($"Site {id} may contain at most {MaxPathPrefixesPerSite} path prefixes");
            var normalizedPaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var path in pathPrefixes)
            {
                var rawPath = (path ?? "").Trim();
                if (rawPath.Length == 0 || rawPath.Length > 256 || !rawPath.StartsWith('/') ||
                    rawPath.Contains('?') || rawPath.Contains('#') || rawPath.Contains('\0'))
                    errors.Add($"Site {id} contains an invalid path prefix");
                else if (!normalizedPaths.Add(NormalizePath(rawPath)))
                    errors.Add($"Site {id} contains duplicate path prefixes after normalization");
            }
            generatedRoutes += (long)normalizedHosts.Length * normalizedPaths.Count;
            generatedDestinations += upstreamSections.Length;

            var allowedHosts = site.GetSection("Egress:AllowedHosts").Get<string[]>() ?? [];
            var allowedPorts = site.GetSection("Egress:AllowedPorts").Get<int[]>() ?? [];
            var allowedSchemes = site.GetSection("Egress:AllowedSchemes").Get<string[]>() ?? [];
            if (allowedHosts.Length > MaxEgressHostsPerSite)
                errors.Add($"Site {id} may contain at most {MaxEgressHostsPerSite} egress hosts");
            if (allowedPorts.Length > MaxEgressPortsPerSite)
                errors.Add($"Site {id} may contain at most {MaxEgressPortsPerSite} egress ports");
            if (allowedSchemes.Length > MaxEgressSchemesPerSite)
                errors.Add($"Site {id} may contain at most {MaxEgressSchemesPerSite} egress schemes");
            if (allowedHosts.Any(x => x == "*" || NormalizeHostname(x).Length == 0))
                errors.Add($"Site {id} egress host entries must be explicit hostnames without wildcards");
            if (allowedPorts.Any(x => x is < 1 or > 65535))
                errors.Add($"Site {id} egress port entries must be between 1 and 65535");
            if (allowedSchemes.Any(x => !string.Equals(x, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                                        !string.Equals(x, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
                errors.Add($"Site {id} egress scheme entries must be HTTP or HTTPS");
            var normalizedEgressHosts = allowedHosts.Select(NormalizeHostname).Where(x => x.Length > 0).ToArray();
            if (normalizedEgressHosts.Length != normalizedEgressHosts.Distinct(StringComparer.OrdinalIgnoreCase).Count())
                errors.Add($"Site {id} contains duplicate egress hosts after normalization");
            if (allowedPorts.Length != allowedPorts.Distinct().Count())
                errors.Add($"Site {id} contains duplicate egress ports");
            var normalizedEgressSchemes = allowedSchemes.Select(x => (x ?? "").Trim().ToLowerInvariant()).ToArray();
            if (normalizedEgressSchemes.Length != normalizedEgressSchemes.Distinct(StringComparer.Ordinal).Count())
                errors.Add($"Site {id} contains duplicate egress schemes");

            if (enforcing)
            {
                if (allowedHosts.Length == 0 || allowedPorts.Length == 0 || allowedSchemes.Length == 0 ||
                    allowedSchemes.Any(x => !string.Equals(x, "https", StringComparison.OrdinalIgnoreCase)))
                    errors.Add($"Site {id} must define an explicit HTTPS egress allowlist");
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

        if (generatedRoutes > maxGeneratedRoutes)
            errors.Add($"Configuration may generate at most {maxGeneratedRoutes} routes");
        if (generatedDestinations > maxGeneratedDestinations)
            errors.Add($"Configuration may generate at most {maxGeneratedDestinations} destinations");

        if (enforcing && activeSites == 0)
            errors.Add("Production requires at least one active Site");

        return errors;
    }

    /// <summary>
    /// Validates an already-bound snapshot. This is the shared gate for database snapshots,
    /// API mutations, and the final configuration consumed by YARP.
    /// </summary>
    public static IReadOnlyList<string> ValidateSnapshot(SiteConfigurationSnapshot snapshot, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(configuration);

        var errors = new List<string>();
        var enforcing = configuration.GetValue("Waf:Deployment:Enforce", false);
        var configuredPolicies = configuration.GetSection("Waf:Policies").GetChildren()
            .Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var activeHostnames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sites = snapshot.Sites ?? [];
        var maxSites = ResolveMaxSites(configuration);
        var maxGeneratedRoutes = ResolveMaxGeneratedRoutes(configuration);
        var maxGeneratedDestinations = ResolveMaxGeneratedDestinations(configuration);
        long generatedRoutes = 0;
        long generatedDestinations = 0;
        if (sites.Count > maxSites)
            errors.Add($"Site snapshot may contain at most {maxSites} Sites");

        foreach (var site in sites)
        {
            var id = NormalizeId(site.Id ?? "");
            if (!RegexPatterns.SiteId.IsMatch(id))
                errors.Add($"Site {site.Id} has an invalid ID");

            if (!site.Enabled)
                continue;

            if (site.Hostnames is null || site.Hostnames.Count == 0)
                errors.Add($"Site {id} must define at least one hostname");
            else if (site.Hostnames.Count > MaxHostnamesPerSite)
                errors.Add($"Site {id} may contain at most {MaxHostnamesPerSite} hostnames");

            foreach (var rawHostname in site.Hostnames ?? [])
            {
                var hostname = NormalizeHostname(rawHostname ?? "");
                if (hostname.Length == 0)
                {
                    errors.Add($"Site {id} contains an invalid hostname");
                    continue;
                }
                if (activeHostnames.TryGetValue(hostname, out var other))
                    errors.Add($"Site hostname {hostname} is already assigned to {other}");
                else
                    activeHostnames[hostname] = id;
            }

            if (site.PathPrefixes is null || site.PathPrefixes.Count == 0)
                errors.Add($"Site {id} must define at least one path prefix");
            else if (site.PathPrefixes.Count > MaxPathPrefixesPerSite)
                errors.Add($"Site {id} may contain at most {MaxPathPrefixesPerSite} path prefixes");
            var normalizedPaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var path in site.PathPrefixes ?? [])
            {
                var rawPath = (path ?? "").Trim();
                if (rawPath.Length == 0 || rawPath.Length > 256 || !rawPath.StartsWith('/') ||
                    rawPath.Contains('?') || rawPath.Contains('#') || rawPath.Contains('\0'))
                    errors.Add($"Site {id} contains an invalid path prefix");
                else if (!normalizedPaths.Add(NormalizePath(rawPath)))
                    errors.Add($"Site {id} contains duplicate path prefixes after normalization");
            }

            if (site.Upstreams is null || site.Upstreams.Count == 0)
                errors.Add($"Site {id} must define at least one upstream");
            else if (site.Upstreams.Count > MaxUpstreamsPerSite)
                errors.Add($"Site {id} may contain at most {MaxUpstreamsPerSite} upstreams");

            var upstreamIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var upstream in site.Upstreams ?? [])
            {
                if (string.IsNullOrWhiteSpace(upstream.Id) || !upstreamIds.Add(upstream.Id.Trim()))
                    errors.Add($"Site {id} upstream IDs must be non-empty and unique");
                var address = upstream.Address;
                if (address is null || !address.IsAbsoluteUri ||
                    (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps))
                    errors.Add($"Site {id} upstream {upstream.Id} must be an absolute HTTP(S) URL");
                else
                {
                    if (!string.IsNullOrEmpty(address.UserInfo))
                        errors.Add($"Site {id} upstream {upstream.Id} must not contain credentials");
                    if (upstream.Weight is < 1 or > 10_000)
                        errors.Add($"Site {id} upstream {upstream.Id} weight must be between 1 and 10000");
                    if (enforcing && IPAddress.TryParse(address.Host, out var literal) && IsDisallowedLiteral(literal))
                        errors.Add($"Site {id} upstream {upstream.Id} must not target a private, loopback, metadata, multicast, or special-use IP literal");
                }
            }

            var policy = string.IsNullOrWhiteSpace(site.WafPolicy) ? "default" : site.WafPolicy.Trim();
            if (enforcing && !string.Equals(policy, "default", StringComparison.OrdinalIgnoreCase) &&
                !configuredPolicies.Contains(policy))
                errors.Add($"Site {id} references undefined WafPolicy {policy}");

            var egress = site.Egress;
            if (egress is null)
            {
                errors.Add($"Site {id} must define an egress policy");
            }
            else
            {
                var allowedHosts = egress.AllowedHosts ?? [];
                var allowedPorts = egress.AllowedPorts ?? [];
                var allowedSchemes = egress.AllowedSchemes ?? [];
                if (allowedHosts.Count > MaxEgressHostsPerSite)
                    errors.Add($"Site {id} may contain at most {MaxEgressHostsPerSite} egress hosts");
                if (allowedPorts.Count > MaxEgressPortsPerSite)
                    errors.Add($"Site {id} may contain at most {MaxEgressPortsPerSite} egress ports");
                if (allowedSchemes.Count > MaxEgressSchemesPerSite)
                    errors.Add($"Site {id} may contain at most {MaxEgressSchemesPerSite} egress schemes");
                if (allowedHosts.Count == 0)
                    errors.Add($"Site {id} must define explicit egress hosts");
                if (allowedPorts.Count == 0 || allowedPorts.Any(x => x is < 1 or > 65535))
                    errors.Add($"Site {id} egress ports must be between 1 and 65535");
                if (allowedHosts.Any(x => x == "*" || NormalizeHostname(x ?? "").Length == 0))
                    errors.Add($"Site {id} egress host entries must be explicit hostnames without wildcards");
                if (allowedSchemes.Count == 0 ||
                    allowedSchemes.Any(x => !string.Equals(x, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                                            !string.Equals(x, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
                    errors.Add($"Site {id} egress schemes must be HTTP or HTTPS");
                var normalizedEgressHosts = allowedHosts.Select(NormalizeHostname).Where(x => x.Length > 0).ToArray();
                if (normalizedEgressHosts.Length != normalizedEgressHosts.Distinct(StringComparer.OrdinalIgnoreCase).Count())
                    errors.Add($"Site {id} contains duplicate egress hosts after normalization");
                if (allowedPorts.Count != allowedPorts.Distinct().Count())
                    errors.Add($"Site {id} contains duplicate egress ports");
                var normalizedEgressSchemes = allowedSchemes.Select(x => (x ?? "").Trim().ToLowerInvariant()).ToArray();
                if (normalizedEgressSchemes.Length != normalizedEgressSchemes.Distinct(StringComparer.Ordinal).Count())
                    errors.Add($"Site {id} contains duplicate egress schemes");
                if (enforcing)
                {
                    if (allowedSchemes.Any(x => !string.Equals(x, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
                        errors.Add($"Site {id} must define an explicit HTTPS egress allowlist");
                    if (egress.AllowPrivateExceptions)
                        errors.Add($"Site {id} must not enable private egress exceptions in Production");
                    foreach (var upstream in site.Upstreams ?? [])
                    {
                        if (upstream.Address is null) continue;
                        if (!string.Equals(upstream.Address.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                            errors.Add($"Site {id} upstream {upstream.Id} must use HTTPS in Production");
                        if (!allowedHosts.Contains(upstream.Address.Host, StringComparer.OrdinalIgnoreCase) ||
                            !allowedPorts.Contains(upstream.Address.Port) ||
                            !allowedSchemes.Contains(upstream.Address.Scheme, StringComparer.OrdinalIgnoreCase))
                            errors.Add($"Site {id} egress policy must cover upstream {upstream.Id}");
                    }
                }
            }

            var health = site.Health ?? new SiteHealthPolicy();
            if (health.Path.Length is < 1 or > 128 || !health.Path.StartsWith('/') ||
                health.Path.Contains('?') || health.Path.Contains('#') || health.Path.Contains('\0'))
                errors.Add($"Site {id} health path must be a relative path without query or fragment");
            if (health.IntervalSeconds is < 5 or > 300)
                errors.Add($"Site {id} health interval must be between 5 and 300 seconds");
            if (health.TimeoutSeconds is < 1 or > 30 || health.TimeoutSeconds >= health.IntervalSeconds)
                errors.Add($"Site {id} health timeout must be between 1 and 30 seconds and less than the health interval");
            if (health.PassiveReactivationSeconds is < 5 or > 3600)
                errors.Add($"Site {id} passive health reactivation must be between 5 and 3600 seconds");

            if (site.RateLimit.Requests is < 1 or > 1_000_000)
                errors.Add($"Site {id} rate requests must be between 1 and 1000000");
            if (site.RateLimit.WindowSeconds is < 1 or > 3600)
                errors.Add($"Site {id} rate window must be between 1 and 3600 seconds");
            if (site.RateLimit.GlobalRequests is < 1 or > 10_000_000)
                errors.Add($"Site {id} global rate limit must be between 1 and 10000000");

            generatedRoutes += (long)(site.Hostnames ?? [])
                .Select(NormalizeHostname)
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() * normalizedPaths.Count;
            generatedDestinations += (site.Upstreams ?? []).Count;
        }

        if (generatedRoutes > maxGeneratedRoutes)
            errors.Add($"Site snapshot may generate at most {maxGeneratedRoutes} routes");
        if (generatedDestinations > maxGeneratedDestinations)
            errors.Add($"Site snapshot may generate at most {maxGeneratedDestinations} destinations");

        if (enforcing && sites.Count(x => x.Enabled) == 0)
            errors.Add("Production requires at least one active Site");
        return errors;
    }

    public static int ResolveMaxSites(IConfiguration configuration) =>
        Math.Clamp(configuration.GetValue("Waf:Sites:MaxSites", DefaultMaxSites), 1, MaximumAllowedSites);

    public static int ResolveMaxGeneratedRoutes(IConfiguration configuration) =>
        Math.Clamp(configuration.GetValue("Waf:Sites:MaxGeneratedRoutes", DefaultMaxGeneratedRoutes), 1, MaximumAllowedGeneratedRoutes);

    public static int ResolveMaxGeneratedDestinations(IConfiguration configuration) =>
        Math.Clamp(configuration.GetValue("Waf:Sites:MaxGeneratedDestinations", DefaultMaxGeneratedDestinations), 1, MaximumAllowedGeneratedDestinations);

    internal static string NormalizeId(string value) =>
        value.Trim().ToLowerInvariant();

    private static class RegexPatterns
    {
        internal static readonly System.Text.RegularExpressions.Regex SiteId =
            new("^[a-z0-9][a-z0-9_-]{0,63}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }

    internal static string NormalizeHostname(string value)
    {
        var hostname = (value ?? "").Trim().TrimEnd('.').ToLowerInvariant();
        if (hostname.Length == 0 || hostname.Length > 253) return string.Empty;
        if (IPAddress.TryParse(hostname, out var address)) return address.ToString().ToLowerInvariant();

        try
        {
            var ascii = HostnameIdn.GetAscii(hostname).ToLowerInvariant();
            if (ascii.Length == 0 || ascii.Length > 253 ||
                ascii.Split('.').Any(label => label.Length is 0 or > 63))
                return string.Empty;
            return Uri.CheckHostName(ascii) == UriHostNameType.Dns ? ascii : string.Empty;
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
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
