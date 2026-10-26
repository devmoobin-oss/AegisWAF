namespace AegisWAF.Gateway.Sites;

public sealed record SiteUpstream(string Id, Uri Address, int Weight = 100);

public sealed record SiteRateLimitPolicy(int Requests = 300, int WindowSeconds = 60, int GlobalRequests = 10_000);

public sealed record SiteEgressPolicy(
    IReadOnlyList<string> AllowedHosts,
    IReadOnlyList<int> AllowedPorts,
    IReadOnlyList<string> AllowedSchemes,
    bool AllowPrivateExceptions = false);

public sealed record SiteHealthPolicy(
    string Path = "/",
    int IntervalSeconds = 10,
    int TimeoutSeconds = 3,
    int PassiveReactivationSeconds = 30);

public sealed record GatewaySite(
    string Id,
    bool Enabled,
    IReadOnlyList<string> Hostnames,
    IReadOnlyList<string> PathPrefixes,
    IReadOnlyList<SiteUpstream> Upstreams,
    string WafPolicy,
    SiteRateLimitPolicy RateLimit,
    SiteEgressPolicy Egress,
    SiteHealthPolicy? Health = null,
    long Version = 1);

public sealed record SiteConfigurationSnapshot(
    IReadOnlyList<GatewaySite> Sites,
    bool UsedLegacyConfiguration);
