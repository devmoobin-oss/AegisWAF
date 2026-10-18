using System.Security.Cryptography;
using System.Text;

namespace AegisWAF.Gateway.Sites;

public sealed class SiteRegistry
{
    public SiteRegistry(SiteConfigurationSnapshot snapshot) => Current = snapshot;

    public SiteConfigurationSnapshot Current { get; }

    public SiteMatchResult? Resolve(string? hostHeader, string? path)
    {
        var match = SiteMatcher.Match(Current, hostHeader, path);
        if (match is null && Current.UsedLegacyConfiguration)
        {
            var fallback = Current.Sites.SingleOrDefault(x => x.Id == "default-site");
            if (fallback is not null)
                return new SiteMatchResult(fallback, hostHeader?.Split(':')[0].Trim().ToLowerInvariant() ?? "", "/");
        }
        return match;
    }
}

public static class SiteProxyConfiguration
{
    public static Dictionary<string, string?> Build(SiteConfigurationSnapshot snapshot, IConfiguration? configuration = null)
    {
        var values = new Dictionary<string, string?>();
        if (snapshot.UsedLegacyConfiguration) return values;

        // No legacy route may compete with explicit Site routes, including custom routes beyond the default route.
        foreach (var route in configuration?.GetSection("ReverseProxy:Routes").GetChildren() ?? [])
            values[$"ReverseProxy:Routes:{route.Key}:Match:Hosts:0"] = "__aegiswaf_legacy_route_disabled__";
        foreach (var site in snapshot.Sites.Where(x => x.Enabled))
        {
            var clusterId = "site-" + site.Id;
            foreach (var hostname in site.Hostnames)
            {
                foreach (var prefix in site.PathPrefixes)
                {
                    var routeKey = hostname + "\0" + prefix;
                    var routeId = site.Hostnames.Count == 1 && prefix == "/"
                        ? clusterId
                        : clusterId + "-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(routeKey)))[..10].ToLowerInvariant();
                    values[$"ReverseProxy:Routes:{routeId}:ClusterId"] = clusterId;
                    values[$"ReverseProxy:Routes:{routeId}:Match:Hosts:0"] = hostname;
                    values[$"ReverseProxy:Routes:{routeId}:Match:Path"] = prefix == "/" ? "{**catch-all}" : prefix + "/{**remainder}";
                    values[$"ReverseProxy:Routes:{routeId}:Order"] = (1000 - prefix.Length).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            foreach (var upstream in site.Upstreams)
            {
                values[$"ReverseProxy:Clusters:{clusterId}:Destinations:{upstream.Id}:Address"] = upstream.Address.ToString();
                values[$"ReverseProxy:Clusters:{clusterId}:Destinations:{upstream.Id}:Metadata:SiteId"] = site.Id;
            }
        }
        return values;
    }
}
