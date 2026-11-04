using System.Text.Json;
using System.Text.Json.Nodes;

namespace AegisWAF.Gateway.Sites;

public sealed record SiteManagementRequest(
    string Id,
    string[] Hostnames,
    string[] PathPrefixes,
    string UpstreamAddress,
    string WafPolicy,
    string[] AllowedHosts,
    int[] AllowedPorts,
    string[] AllowedSchemes,
    bool Enabled = true)
{
    public SiteUpstreamManagementRequest[]? Upstreams { get; init; }
    public SiteHealthPolicy? Health { get; init; }
    public SiteRateLimitPolicy? RateLimit { get; init; }
    public long? ExpectedVersion { get; init; }
    public bool AllowPrivateExceptions { get; init; }
}

public sealed record SiteUpstreamManagementRequest(string Id, string Address, int Weight = 100);

/// <summary>Validates and edits only the local Site configuration document. Secrets are never copied to it.</summary>
public static class SiteConfigurationEditor
{
    public static bool TryNormalize(SiteManagementRequest input, out SiteManagementRequest normalized, out string error)
    {
        normalized = input;
        error = "";
        var id = (input.Id ?? "").Trim().ToLowerInvariant();
        if (!SiteConfigurationValidator.IsValidSiteId(id)) { error = "Site ID must use 1-64 lowercase letters, numbers, hyphen, or underscore"; return false; }
        if (input.ExpectedVersion is < 1) { error = "Expected Site version must be a positive integer"; return false; }

        var rawHosts = input.Hostnames ?? [];
        var normalizedHosts = rawHosts.Select(NormalizeHostname).ToArray();
        if (normalizedHosts.Any(x => x.Length == 0))
        { error = "Site contains an invalid hostname"; return false; }
        var hosts = normalizedHosts.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (hosts.Length == 0) { error = "At least one hostname is required"; return false; }
        if (hosts.Length > 32) { error = "A Site may contain at most 32 hostnames"; return false; }

        var upstreamInputs = input.Upstreams is { Length: > 0 }
            ? input.Upstreams
            : [new SiteUpstreamManagementRequest("primary", input.UpstreamAddress ?? "", 100)];
        if (upstreamInputs.Length > 32) { error = "A Site may contain at most 32 upstreams"; return false; }
        var upstreams = new List<SiteUpstreamManagementRequest>();
        var upstreamIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in upstreamInputs)
        {
            var upstreamId = SiteConfigurationValidator.NormalizeUpstreamId(candidate.Id);
            if (!SiteConfigurationValidator.IsValidUpstreamId(upstreamId))
            { error = "Upstream ID must use 1-64 lowercase letters, numbers, hyphen, or underscore"; return false; }
            if (!upstreamIds.Add(upstreamId))
            { error = "Upstream IDs must be non-empty and unique"; return false; }
            if (!Uri.TryCreate(candidate.Address?.Trim(), UriKind.Absolute, out var address) ||
                (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps) ||
                !string.IsNullOrEmpty(address.UserInfo))
            { error = "Every upstream must be an absolute HTTP(S) URL without credentials"; return false; }
            if (!string.IsNullOrEmpty(address.Query) || !string.IsNullOrEmpty(address.Fragment))
            { error = "Every upstream must not contain a query or fragment; put health/path data in the base path only"; return false; }
            if (candidate.Weight is < 1 or > SiteConfigurationValidator.MaxUpstreamWeight)
            { error = $"Upstream weight must be between 1 and {SiteConfigurationValidator.MaxUpstreamWeight}"; return false; }
            upstreams.Add(new SiteUpstreamManagementRequest(upstreamId, address.ToString(), candidate.Weight));
        }
        var firstUri = new Uri(upstreams[0].Address, UriKind.Absolute);

        var paths = (input.PathPrefixes ?? ["/"]).Select(SiteConfigurationValidator.NormalizePath).Distinct(StringComparer.Ordinal).ToArray();
        var rawAllowedHosts = input.AllowedHosts ?? [];
        var normalizedAllowedHosts = rawAllowedHosts.Select(NormalizeHostname).ToArray();
        if (rawAllowedHosts.Length > 0 &&
            (normalizedAllowedHosts.Any(x => x.Length == 0) ||
             normalizedAllowedHosts.Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalizedAllowedHosts.Length))
        { error = "Egress host entries must be valid, explicit, and unique hostnames"; return false; }
        var allowedHosts = normalizedAllowedHosts.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (allowedHosts.Length == 0) allowedHosts = upstreams.Select(x => NormalizeHostname(new Uri(x.Address).Host)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        var rawAllowedPorts = input.AllowedPorts ?? [];
        if (rawAllowedPorts.Length > 0 &&
            (rawAllowedPorts.Any(x => x is < 1 or > 65535) || rawAllowedPorts.Distinct().Count() != rawAllowedPorts.Length))
        { error = "Egress ports must be between 1 and 65535 and unique"; return false; }
        var allowedPorts = rawAllowedPorts.Distinct().ToArray();
        if (allowedPorts.Length == 0) allowedPorts = upstreams.Select(x => new Uri(x.Address).Port).Distinct().ToArray();

        var rawAllowedSchemes = input.AllowedSchemes ?? [];
        var normalizedSchemes = rawAllowedSchemes.Select(x => (x ?? "").Trim().ToLowerInvariant()).ToArray();
        if (rawAllowedSchemes.Length > 0 &&
            (normalizedSchemes.Any(x => x != Uri.UriSchemeHttp && x != Uri.UriSchemeHttps) ||
             normalizedSchemes.Distinct(StringComparer.Ordinal).Count() != normalizedSchemes.Length))
        { error = "Egress schemes must be HTTP or HTTPS and unique"; return false; }
        var schemes = normalizedSchemes.Distinct(StringComparer.Ordinal).ToArray();
        if (schemes.Length == 0) schemes = upstreams.Select(x => new Uri(x.Address).Scheme.ToLowerInvariant()).Distinct().ToArray();

        normalized = new SiteManagementRequest(id, hosts, paths, firstUri.ToString(), string.IsNullOrWhiteSpace(input.WafPolicy) ? "default" : input.WafPolicy.Trim(), allowedHosts, allowedPorts, schemes, input.Enabled)
        {
            Upstreams = upstreams.ToArray(),
            Health = input.Health,
            RateLimit = input.RateLimit,
            ExpectedVersion = input.ExpectedVersion,
            AllowPrivateExceptions = input.AllowPrivateExceptions
        };
        return true;
    }

    public static JsonObject ToJson(SiteManagementRequest request)
    {
        var upstreams = request.Upstreams is { Length: > 0 }
            ? request.Upstreams
            : [new SiteUpstreamManagementRequest("primary", request.UpstreamAddress, 100)];
        var defaultHealthPath = new Uri(upstreams[0].Address, UriKind.Absolute).AbsolutePath.TrimEnd('/');
        var health = request.Health ?? new SiteHealthPolicy(defaultHealthPath.Length == 0 ? "/" : defaultHealthPath);
        var rate = request.RateLimit ?? new SiteRateLimitPolicy();
        return new JsonObject
        {
            ["Enabled"] = request.Enabled,
            ["Hostnames"] = new JsonArray(request.Hostnames.Select(x => JsonValue.Create(x)).ToArray()),
            ["PathPrefixes"] = new JsonArray(request.PathPrefixes.Select(x => JsonValue.Create(x)).ToArray()),
            ["WafPolicy"] = request.WafPolicy,
            ["Upstreams"] = new JsonObject(upstreams.ToDictionary(x => x.Id, x => (JsonNode?)new JsonObject
            {
                ["Address"] = x.Address,
                ["Weight"] = x.Weight
            }, StringComparer.OrdinalIgnoreCase)),
            ["Egress"] = new JsonObject
            {
                ["AllowedHosts"] = new JsonArray(request.AllowedHosts.Select(x => JsonValue.Create(x)).ToArray()),
                ["AllowedPorts"] = new JsonArray(request.AllowedPorts.Select(x => JsonValue.Create(x)).ToArray()),
                ["AllowedSchemes"] = new JsonArray(request.AllowedSchemes.Select(x => JsonValue.Create(x)).ToArray()),
                ["AllowPrivateExceptions"] = request.AllowPrivateExceptions
            },
            ["Health"] = new JsonObject
            {
                ["Path"] = health.Path,
                ["IntervalSeconds"] = health.IntervalSeconds,
                ["TimeoutSeconds"] = health.TimeoutSeconds,
                ["PassiveReactivationSeconds"] = health.PassiveReactivationSeconds
            },
            ["RateLimit"] = new JsonObject
            {
                ["Requests"] = rate.Requests,
                ["WindowSeconds"] = rate.WindowSeconds,
                ["GlobalRequests"] = rate.GlobalRequests
            }
        };
    }

    public static void Merge(JsonObject root, SiteManagementRequest request)
    {
        var sites = root["Sites"] as JsonObject ?? new JsonObject();
        root["Sites"] = sites;
        sites[request.Id] = ToJson(request);
    }

    public static void Remove(JsonObject root, string id)
    {
        var sites = root["Sites"] as JsonObject ?? new JsonObject();
        root["Sites"] = sites;
        // A disabled override also masks a Site inherited from appsettings.json.
        sites[id.Trim().ToLowerInvariant()] = new JsonObject { ["Enabled"] = false };
    }

    public static bool AreEquivalent(GatewaySite left, GatewaySite right)
    {
        return left.Id.Equals(right.Id, StringComparison.OrdinalIgnoreCase)
            && left.Enabled == right.Enabled
            && SequenceEqual(left.Hostnames, right.Hostnames, StringComparer.OrdinalIgnoreCase)
            && SequenceEqual(left.PathPrefixes, right.PathPrefixes, StringComparer.Ordinal)
            && left.WafPolicy.Equals(right.WafPolicy, StringComparison.OrdinalIgnoreCase)
            && left.RateLimit == right.RateLimit
            && left.Egress.AllowPrivateExceptions == right.Egress.AllowPrivateExceptions
            && SequenceEqual(left.Egress.AllowedHosts, right.Egress.AllowedHosts, StringComparer.OrdinalIgnoreCase)
            && SequenceEqual(left.Egress.AllowedPorts, right.Egress.AllowedPorts)
            && SequenceEqual(left.Egress.AllowedSchemes, right.Egress.AllowedSchemes, StringComparer.OrdinalIgnoreCase)
            && UpstreamsEqual(left.Upstreams, right.Upstreams)
            && Equals(left.Health, right.Health);
    }

    private static bool UpstreamsEqual(IReadOnlyList<SiteUpstream> left, IReadOnlyList<SiteUpstream> right)
    {
        if (left.Count != right.Count) return false;
        for (var i = 0; i < left.Count; i++)
        {
            if (!left[i].Id.Equals(right[i].Id, StringComparison.OrdinalIgnoreCase) ||
                !left[i].Address.Equals(right[i].Address) || left[i].Weight != right[i].Weight)
                return false;
        }
        return true;
    }

    private static bool SequenceEqual<T>(IReadOnlyList<T> left, IReadOnlyList<T> right, IEqualityComparer<T>? comparer = null) =>
        left.SequenceEqual(right, comparer);

    public static async Task<JsonObject> LoadAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) return new JsonObject();
        await using var stream = File.OpenRead(path);
        var node = await JsonNode.ParseAsync(stream, cancellationToken: ct);
        return node as JsonObject ?? throw new InvalidDataException("appsettings.local.json must contain a JSON object");
    }

    public static async Task SaveAsync(string path, JsonObject root, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Configuration directory is unavailable");
        Directory.CreateDirectory(directory);
        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllTextAsync(temp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), ct);
            File.Move(temp, path, true);
        }
        finally { try { if (File.Exists(temp)) File.Delete(temp); } catch { } }
    }

    private static string NormalizeHostname(string value)
        => SiteConfigurationValidator.NormalizeHostname(value);
}
