using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

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
    bool Enabled = true);

/// <summary>Validates and edits only the local Site configuration document. Secrets are never copied to it.</summary>
public static class SiteConfigurationEditor
{
    private static readonly Regex IdPattern = new("^[a-z0-9][a-z0-9_-]{0,63}$", RegexOptions.CultureInvariant);

    public static bool TryNormalize(SiteManagementRequest input, out SiteManagementRequest normalized, out string error)
    {
        normalized = input;
        error = "";
        var id = (input.Id ?? "").Trim().ToLowerInvariant();
        if (!IdPattern.IsMatch(id)) { error = "Site ID must use 1-64 lowercase letters, numbers, hyphen, or underscore"; return false; }

        var hosts = (input.Hostnames ?? []).Select(NormalizeHostname).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (hosts.Length == 0) { error = "At least one hostname is required"; return false; }
        if (hosts.Length > 32) { error = "A Site may contain at most 32 hostnames"; return false; }

        if (!Uri.TryCreate(input.UpstreamAddress?.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        { error = "Upstream must be an absolute HTTP(S) URL without credentials"; return false; }

        var paths = (input.PathPrefixes ?? ["/"]).Select(SiteConfigurationValidator.NormalizePath).Distinct(StringComparer.Ordinal).ToArray();
        var allowedHosts = (input.AllowedHosts ?? []).Select(NormalizeHostname).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (allowedHosts.Length == 0) allowedHosts = [NormalizeHostname(uri.Host)];
        var allowedPorts = (input.AllowedPorts ?? []).Distinct().ToArray();
        if (allowedPorts.Length == 0) allowedPorts = [uri.Port > 0 ? uri.Port : uri.Scheme == Uri.UriSchemeHttps ? 443 : 80];
        var schemes = (input.AllowedSchemes ?? []).Select(x => (x ?? "").Trim().ToLowerInvariant()).Where(x => x.Length > 0).Distinct().ToArray();
        if (schemes.Length == 0) schemes = [uri.Scheme.ToLowerInvariant()];

        normalized = new SiteManagementRequest(id, hosts, paths, uri.ToString(), string.IsNullOrWhiteSpace(input.WafPolicy) ? "default" : input.WafPolicy.Trim(), allowedHosts, allowedPorts, schemes, input.Enabled);
        return true;
    }

    public static JsonObject ToJson(SiteManagementRequest request)
    {
        var upstreamPath = new Uri(request.UpstreamAddress, UriKind.Absolute).AbsolutePath.TrimEnd('/');
        var healthPath = upstreamPath.Length == 0 ? "/" : upstreamPath;
        var upstream = new JsonObject { ["Address"] = request.UpstreamAddress, ["Weight"] = 100 };
        return new JsonObject
        {
            ["Enabled"] = request.Enabled,
            ["Hostnames"] = new JsonArray(request.Hostnames.Select(x => JsonValue.Create(x)).ToArray()),
            ["PathPrefixes"] = new JsonArray(request.PathPrefixes.Select(x => JsonValue.Create(x)).ToArray()),
            ["WafPolicy"] = request.WafPolicy,
            ["Upstreams"] = new JsonObject { ["primary"] = upstream },
            ["Egress"] = new JsonObject
            {
                ["AllowedHosts"] = new JsonArray(request.AllowedHosts.Select(x => JsonValue.Create(x)).ToArray()),
                ["AllowedPorts"] = new JsonArray(request.AllowedPorts.Select(x => JsonValue.Create(x)).ToArray()),
                ["AllowedSchemes"] = new JsonArray(request.AllowedSchemes.Select(x => JsonValue.Create(x)).ToArray()),
                ["AllowPrivateExceptions"] = false
            },
            ["Health"] = new JsonObject { ["Path"] = healthPath, ["IntervalSeconds"] = 10, ["TimeoutSeconds"] = 3, ["PassiveReactivationSeconds"] = 30 }
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
    {
        var host = (value ?? "").Trim().TrimEnd('.').ToLowerInvariant();
        return Uri.CheckHostName(host) == UriHostNameType.Unknown || host.Contains('*') ? "" : host;
    }
}
