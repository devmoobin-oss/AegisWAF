namespace AegisWAF.Gateway.Sites;

internal sealed record SitePersistenceRow(
    string SiteId,
    bool Enabled,
    string[] Hostnames,
    string[] PathPrefixes,
    string[] UpstreamIds,
    string[] UpstreamAddresses,
    int[] UpstreamWeights,
    string WafPolicy,
    string[] EgressAllowedHosts,
    int[] EgressAllowedPorts,
    string[] EgressAllowedSchemes,
    bool EgressAllowPrivateExceptions,
    string HealthPath,
    int HealthIntervalSeconds,
    int HealthTimeoutSeconds,
    int HealthPassiveReactivationSeconds,
    int RateRequests,
    int RateWindowSeconds,
    int RateGlobalRequests,
    long Version = 1);

internal static class SitePersistenceCodec
{
    internal static SitePersistenceRow ToRow(GatewaySite site)
    {
        ArgumentNullException.ThrowIfNull(site);
        if (site.Hostnames is null || site.PathPrefixes is null || site.Upstreams is null ||
            site.RateLimit is null || site.Egress is null)
            throw new InvalidDataException($"Site {site.Id} contains null required configuration");
        if (site.Upstreams.Any(x => x is null))
            throw new InvalidDataException($"Site {site.Id} contains a null upstream");
        if (site.Egress.AllowedHosts is null || site.Egress.AllowedPorts is null || site.Egress.AllowedSchemes is null)
            throw new InvalidDataException($"Site {site.Id} contains a null egress collection");
        foreach (var upstream in site.Upstreams)
        {
            var upstreamId = SiteConfigurationValidator.NormalizeUpstreamId(upstream.Id);
            if (!SiteConfigurationValidator.IsValidUpstreamId(upstreamId))
                throw new InvalidDataException($"Site {site.Id} upstream {upstream.Id} has an invalid ID");
            if (!upstream.Address.IsAbsoluteUri ||
                (upstream.Address.Scheme != Uri.UriSchemeHttp && upstream.Address.Scheme != Uri.UriSchemeHttps))
                throw new InvalidDataException($"Site {site.Id} upstream {upstream.Id} must be an absolute HTTP(S) URL");
            if (!string.IsNullOrEmpty(upstream.Address.UserInfo))
                throw new InvalidDataException($"Site {site.Id} upstream {upstream.Id} must not contain credentials");
            if (!string.IsNullOrEmpty(upstream.Address.Query) || !string.IsNullOrEmpty(upstream.Address.Fragment))
                throw new InvalidDataException($"Site {site.Id} upstream {upstream.Id} must not contain a query or fragment; use the base path only");
            if (upstream.Weight is < 1 or > SiteConfigurationValidator.MaxUpstreamWeight)
                throw new InvalidDataException($"Site {site.Id} upstream {upstream.Id} weight is outside the allowed range");
        }
        var health = site.Health ?? new SiteHealthPolicy();
        var row = new SitePersistenceRow(
            SiteConfigurationValidator.NormalizeId(site.Id),
            site.Enabled,
            site.Hostnames.ToArray(),
            site.PathPrefixes.ToArray(),
            site.Upstreams.Select(x => SiteConfigurationValidator.NormalizeUpstreamId(x.Id)).ToArray(),
            site.Upstreams.Select(x => x.Address.ToString()).ToArray(),
            site.Upstreams.Select(x => x.Weight).ToArray(),
            site.WafPolicy,
            site.Egress.AllowedHosts.ToArray(),
            site.Egress.AllowedPorts.ToArray(),
            site.Egress.AllowedSchemes.ToArray(),
            site.Egress.AllowPrivateExceptions,
            health.Path,
            health.IntervalSeconds,
            health.TimeoutSeconds,
            health.PassiveReactivationSeconds,
            site.RateLimit.Requests,
            site.RateLimit.WindowSeconds,
            site.RateLimit.GlobalRequests,
            site.Version);

        _ = FromRow(row);
        return row;
    }

    internal static GatewaySite FromRow(SitePersistenceRow row)
    {
        if (row is null)
            throw new InvalidDataException("Persisted Site row is null");
        var siteId = SiteConfigurationValidator.NormalizeId(row.SiteId ?? "");
        if (!SiteConfigurationValidator.IsValidSiteId(siteId))
            throw new InvalidDataException($"Persisted Site {row.SiteId} has an invalid ID");
        if (row.Hostnames is null || row.PathPrefixes is null || row.UpstreamIds is null ||
            row.UpstreamAddresses is null || row.UpstreamWeights is null ||
            row.EgressAllowedHosts is null || row.EgressAllowedPorts is null || row.EgressAllowedSchemes is null)
            throw new InvalidDataException($"Persisted Site {row.SiteId} contains a null array");
        if (row.Hostnames.Length > SiteConfigurationValidator.MaxHostnamesPerSite)
            throw new InvalidDataException($"Persisted Site {row.SiteId} may contain at most {SiteConfigurationValidator.MaxHostnamesPerSite} hostnames");
        if (row.PathPrefixes.Length > SiteConfigurationValidator.MaxPathPrefixesPerSite)
            throw new InvalidDataException($"Persisted Site {row.SiteId} may contain at most {SiteConfigurationValidator.MaxPathPrefixesPerSite} path prefixes");
        if (row.UpstreamIds.Length > SiteConfigurationValidator.MaxUpstreamsPerSite ||
            row.UpstreamAddresses.Length > SiteConfigurationValidator.MaxUpstreamsPerSite ||
            row.UpstreamWeights.Length > SiteConfigurationValidator.MaxUpstreamsPerSite)
            throw new InvalidDataException($"Persisted Site {row.SiteId} may contain at most {SiteConfigurationValidator.MaxUpstreamsPerSite} upstreams");
        if (row.EgressAllowedHosts.Length > SiteConfigurationValidator.MaxEgressHostsPerSite)
            throw new InvalidDataException($"Persisted Site {row.SiteId} may contain at most {SiteConfigurationValidator.MaxEgressHostsPerSite} egress hosts");
        if (row.EgressAllowedPorts.Length > SiteConfigurationValidator.MaxEgressPortsPerSite)
            throw new InvalidDataException($"Persisted Site {row.SiteId} may contain at most {SiteConfigurationValidator.MaxEgressPortsPerSite} egress ports");
        if (row.EgressAllowedSchemes.Length > SiteConfigurationValidator.MaxEgressSchemesPerSite)
            throw new InvalidDataException($"Persisted Site {row.SiteId} may contain at most {SiteConfigurationValidator.MaxEgressSchemesPerSite} egress schemes");

        var hostnames = row.Hostnames.Select(x => SiteConfigurationValidator.NormalizeHostname(x ?? "")).ToArray();
        if (hostnames.Length == 0)
            throw new InvalidDataException($"Persisted Site {siteId} must contain at least one hostname");
        if (hostnames.Any(x => x.Length == 0) || hostnames.Distinct(StringComparer.OrdinalIgnoreCase).Count() != hostnames.Length)
            throw new InvalidDataException($"Persisted Site {row.SiteId} contains invalid or duplicate hostnames");

        if (row.PathPrefixes.Length == 0)
            throw new InvalidDataException($"Persisted Site {siteId} must contain at least one path prefix");
        var pathPrefixes = row.PathPrefixes.Select(x =>
        {
            var raw = (x ?? "").Trim();
            if (raw.Length == 0 || raw.Length > 256 || !raw.StartsWith('/') || raw.Contains('?') || raw.Contains('#') || raw.Contains('\0'))
                throw new InvalidDataException($"Persisted Site {row.SiteId} contains an invalid path prefix");
            return SiteConfigurationValidator.NormalizePath(raw);
        }).ToArray();
        if (pathPrefixes.Distinct(StringComparer.Ordinal).Count() != pathPrefixes.Length)
            throw new InvalidDataException($"Persisted Site {row.SiteId} contains duplicate path prefixes after normalization");

        if (row.UpstreamIds.Length == 0)
            throw new InvalidDataException($"Persisted Site {row.SiteId} must contain at least one upstream");
        if (row.UpstreamIds.Length != row.UpstreamAddresses.Length || row.UpstreamIds.Length != row.UpstreamWeights.Length)
            throw new InvalidDataException($"Persisted Site {row.SiteId} upstream arrays must have the same length");

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var upstreams = new SiteUpstream[row.UpstreamIds.Length];
        for (var i = 0; i < row.UpstreamIds.Length; i++)
        {
            var id = SiteConfigurationValidator.NormalizeUpstreamId(row.UpstreamIds[i]);
            if (!SiteConfigurationValidator.IsValidUpstreamId(id))
                throw new InvalidDataException($"Persisted Site {row.SiteId} upstream {row.UpstreamIds[i]} has an invalid ID");
            if (!ids.Add(id))
                throw new InvalidDataException($"Persisted Site {row.SiteId} upstream IDs must be non-empty and unique");
            if (!Uri.TryCreate(row.UpstreamAddresses[i], UriKind.Absolute, out var address) ||
                (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps))
                throw new InvalidDataException($"Persisted Site {row.SiteId} upstream {id} must be an absolute HTTP(S) URL");
            if (!string.IsNullOrEmpty(address.UserInfo))
                throw new InvalidDataException($"Persisted Site {row.SiteId} upstream {id} must not contain credentials");
            if (!string.IsNullOrEmpty(address.Query) || !string.IsNullOrEmpty(address.Fragment))
                throw new InvalidDataException($"Persisted Site {row.SiteId} upstream {id} must not contain a query or fragment; use the base path only");
            if (row.UpstreamWeights[i] is < 1 or > SiteConfigurationValidator.MaxUpstreamWeight)
                throw new InvalidDataException($"Persisted Site {row.SiteId} upstream {id} weight is outside the allowed range");
            upstreams[i] = new SiteUpstream(id, address, row.UpstreamWeights[i]);
        }

        if (row.HealthPath is null || row.HealthPath.Length is < 1 or > 128 || !row.HealthPath.StartsWith('/') ||
            row.HealthPath.Contains('?') || row.HealthPath.Contains('#') || row.HealthPath.Contains('\0'))
            throw new InvalidDataException($"Persisted Site {row.SiteId} health path is invalid");
        if (row.HealthIntervalSeconds is < 5 or > 300 || row.HealthTimeoutSeconds is < 1 or > 30 ||
            row.HealthTimeoutSeconds >= row.HealthIntervalSeconds || row.HealthPassiveReactivationSeconds is < 5 or > 3600)
            throw new InvalidDataException($"Persisted Site {row.SiteId} health policy is outside the allowed range");
        if (row.RateRequests is < 1 or > 1_000_000 || row.RateWindowSeconds is < 1 or > 3600 ||
            row.RateGlobalRequests is < 1 or > 10_000_000)
            throw new InvalidDataException($"Persisted Site {row.SiteId} rate-limit policy is outside the allowed range");
        if (row.Version < 1)
            throw new InvalidDataException($"Persisted Site {row.SiteId} row version is invalid");

        var egressHosts = row.EgressAllowedHosts.Select(x => SiteConfigurationValidator.NormalizeHostname(x ?? "")).ToArray();
        if (egressHosts.Length == 0 || egressHosts.Any(x => x.Length == 0))
            throw new InvalidDataException($"Persisted Site {row.SiteId} contains an invalid egress host");
        if (egressHosts.Distinct(StringComparer.OrdinalIgnoreCase).Count() != egressHosts.Length)
            throw new InvalidDataException($"Persisted Site {row.SiteId} contains duplicate egress hosts after normalization");

        if (row.EgressAllowedPorts.Length == 0 || row.EgressAllowedPorts.Any(x => x is < 1 or > 65535))
            throw new InvalidDataException($"Persisted Site {row.SiteId} egress ports must be between 1 and 65535");
        if (row.EgressAllowedPorts.Distinct().Count() != row.EgressAllowedPorts.Length)
            throw new InvalidDataException($"Persisted Site {row.SiteId} contains duplicate egress ports");

        var egressSchemes = row.EgressAllowedSchemes.Select(x => (x ?? "").Trim().ToLowerInvariant()).ToArray();
        if (egressSchemes.Length == 0 || egressSchemes.Any(x => x != Uri.UriSchemeHttp && x != Uri.UriSchemeHttps))
            throw new InvalidDataException($"Persisted Site {row.SiteId} egress schemes must be HTTP or HTTPS");
        if (egressSchemes.Distinct(StringComparer.Ordinal).Count() != egressSchemes.Length)
            throw new InvalidDataException($"Persisted Site {row.SiteId} contains duplicate egress schemes");

        return new GatewaySite(
            siteId,
            row.Enabled,
            hostnames,
            pathPrefixes,
            upstreams,
            string.IsNullOrWhiteSpace(row.WafPolicy) ? "default" : row.WafPolicy,
            new SiteRateLimitPolicy(row.RateRequests, row.RateWindowSeconds, row.RateGlobalRequests),
            new SiteEgressPolicy(egressHosts, row.EgressAllowedPorts, egressSchemes, row.EgressAllowPrivateExceptions),
            new SiteHealthPolicy(row.HealthPath, row.HealthIntervalSeconds, row.HealthTimeoutSeconds, row.HealthPassiveReactivationSeconds),
            row.Version);
    }
}
