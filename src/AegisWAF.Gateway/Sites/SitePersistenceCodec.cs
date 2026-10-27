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
        var health = site.Health ?? new SiteHealthPolicy();
        return new SitePersistenceRow(
            site.Id,
            site.Enabled,
            site.Hostnames.ToArray(),
            site.PathPrefixes.ToArray(),
            site.Upstreams.Select(x => x.Id).ToArray(),
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
    }

    internal static GatewaySite FromRow(SitePersistenceRow row)
    {
        if (string.IsNullOrWhiteSpace(row.SiteId))
            throw new InvalidDataException("Persisted Site ID is empty");
        if (row.Hostnames is null || row.PathPrefixes is null || row.UpstreamIds is null ||
            row.UpstreamAddresses is null || row.UpstreamWeights is null ||
            row.EgressAllowedHosts is null || row.EgressAllowedPorts is null || row.EgressAllowedSchemes is null)
            throw new InvalidDataException($"Persisted Site {row.SiteId} contains a null array");

        var hostnames = row.Hostnames.Select(x => SiteConfigurationValidator.NormalizeHostname(x ?? "")).ToArray();
        if (hostnames.Any(x => x.Length == 0) || hostnames.Distinct(StringComparer.OrdinalIgnoreCase).Count() != hostnames.Length)
            throw new InvalidDataException($"Persisted Site {row.SiteId} contains invalid or duplicate hostnames");

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
            var id = row.UpstreamIds[i]?.Trim() ?? "";
            if (id.Length == 0 || !ids.Add(id))
                throw new InvalidDataException($"Persisted Site {row.SiteId} upstream IDs must be non-empty and unique");
            if (!Uri.TryCreate(row.UpstreamAddresses[i], UriKind.Absolute, out var address) ||
                (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps))
                throw new InvalidDataException($"Persisted Site {row.SiteId} upstream {id} must be an absolute HTTP(S) URL");
            if (!string.IsNullOrEmpty(address.UserInfo))
                throw new InvalidDataException($"Persisted Site {row.SiteId} upstream {id} must not contain credentials");
            if (row.UpstreamWeights[i] is < 1 or > 10_000)
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
        if (row.EgressAllowedPorts.Any(x => x is < 1 or > 65535))
            throw new InvalidDataException($"Persisted Site {row.SiteId} egress port is outside the allowed range");

        return new GatewaySite(
            row.SiteId,
            row.Enabled,
            hostnames,
            pathPrefixes,
            upstreams,
            string.IsNullOrWhiteSpace(row.WafPolicy) ? "default" : row.WafPolicy,
            new SiteRateLimitPolicy(row.RateRequests, row.RateWindowSeconds, row.RateGlobalRequests),
            new SiteEgressPolicy(row.EgressAllowedHosts, row.EgressAllowedPorts, row.EgressAllowedSchemes, row.EgressAllowPrivateExceptions),
            new SiteHealthPolicy(row.HealthPath, row.HealthIntervalSeconds, row.HealthTimeoutSeconds, row.HealthPassiveReactivationSeconds),
            row.Version);
    }
}
