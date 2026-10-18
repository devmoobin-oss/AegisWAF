using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace AegisWAF.Gateway;

public sealed record SsrfDecision(bool Blocked, string Reason, string OwaspCode = "A10");

/// <summary>Checks URL-shaped input for SSRF targets without DNS resolution or outbound network access.</summary>
public static partial class SsrfGuard
{
    private static readonly string[] DangerousSchemes = ["file", "gopher", "dict", "ftp", "data", "php", "expect", "jar", "netdoc"];
    private static readonly HashSet<string> MetadataHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "metadata.google.internal", "metadata", "instance-data", "instance-data.ec2.internal", "100.100.100.200"
    };
    private static readonly ConcurrentDictionary<string, (DateTimeOffset Expires, SsrfDecision Decision)> DnsCache = new(StringComparer.OrdinalIgnoreCase);

    public static SsrfDecision Check(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return new(false, "");
        foreach (var variant in DecodeVariants(value))
        {
            foreach (Match match in UrlTokenRegex().Matches(variant))
            {
                var token = match.Value.TrimEnd('.', ',', ';', ')', ']', '}');
                if (!Uri.TryCreate(token, UriKind.Absolute, out var uri)) continue;
                if (DangerousSchemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase))
                    return new(true, $"SSRF: dangerous scheme {uri.Scheme}");
                if (!uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) && !uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)) continue;
                if (IsBlockedHost(uri.Host, out var reason)) return new(true, $"SSRF: {reason}");
            }
        }
        return new(false, "");
    }

    /// <summary>Optional DNS-aware check for Production. It detects hostnames resolving to private targets.</summary>
    public static async Task<SsrfDecision> CheckAsync(string value, bool resolveDns, CancellationToken ct = default)
    {
        var direct = Check(value);
        if (direct.Blocked || !resolveDns || string.IsNullOrWhiteSpace(value)) return direct;
        var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var variant in DecodeVariants(value))
        {
            foreach (Match match in UrlTokenRegex().Matches(variant))
            {
                if (hosts.Count >= 8) break;
                if (!Uri.TryCreate(match.Value.TrimEnd('.', ',', ';', ')', ']', '}'), UriKind.Absolute, out var uri)) continue;
                if (uri.Scheme is not ("http" or "https") || IPAddress.TryParse(uri.Host, out _)) continue;
                hosts.Add(uri.Host);
            }
        }
        foreach (var host in hosts)
        {
            if (DnsCache.TryGetValue(host, out var cached) && cached.Expires > DateTimeOffset.UtcNow)
            {
                if (cached.Decision.Blocked) return cached.Decision;
                continue;
            }
            SsrfDecision decision;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromMilliseconds(250));
                var addresses = await Dns.GetHostAddressesAsync(host, timeout.Token);
                decision = addresses.Any(address => IsBlockedHost(address.ToString(), out _))
                    ? new(true, "SSRF: hostname resolves to private/link-local target")
                    : new(false, "");
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            { decision = new(true, "SSRF: DNS resolution timeout"); }
            catch (SocketException)
            { decision = new(true, "SSRF: DNS resolution failed"); }
            DnsCache[host] = (DateTimeOffset.UtcNow.AddSeconds(30), decision);
            if (DnsCache.Count > 2048)
            {
                var now = DateTimeOffset.UtcNow;
                foreach (var entry in DnsCache)
                    if (entry.Value.Expires <= now) DnsCache.TryRemove(entry.Key, out _);
                if (DnsCache.Count > 2048)
                    foreach (var key in DnsCache.Keys.Take(DnsCache.Count - 2048)) DnsCache.TryRemove(key, out _);
            }
            if (decision.Blocked) return decision;
        }
        return new(false, "");
    }

    private static IEnumerable<string> DecodeVariants(string value)
    {
        var current = value;
        yield return current;
        for (var i = 0; i < 3; i++)
        {
            var next = Uri.UnescapeDataString(current.Replace('+', ' '));
            if (next == current) yield break;
            current = next;
            yield return current;
        }
    }

    private static bool IsBlockedHost(string host, out string reason)
    {
        if (MetadataHosts.Contains(host) || host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase) || host.Equals("host.docker.internal", StringComparison.OrdinalIgnoreCase))
        { reason = "metadata/internal hostname"; return true; }
        if (IPAddress.TryParse(host, out var ip))
        {
            if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
            if (IPAddress.IsLoopback(ip)) { reason = "loopback target"; return true; }
            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                var bytes = ip.GetAddressBytes();
                var value = (uint)(bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3]);
                if (IsPrivateIpv4(value))
                { reason = "private/link-local target"; return true; }
            }
            else if (IsBlockedIpv6(ip))
            { reason = "private/link-local target"; return true; }
        }
        else if (TryParseLegacyIpv4(host, out var legacy) && IsPrivateIpv4(legacy))
        { reason = "non-canonical private target"; return true; }
        reason = ""; return false;
    }

    private static bool IsPrivateIpv4(uint value) => (value & 0xff000000) == 0x0a000000 || (value & 0xfff00000) == 0xac100000 ||
        (value & 0xffff0000) == 0xc0a80000 || (value & 0xffff0000) == 0xa9fe0000 || (value & 0xff000000) == 0x7f000000 || value == 0 ||
        (value & 0xffc00000) == 0x64400000 || // CGNAT 100.64.0.0/10
        (value & 0xff000000) >= 0xe0000000; // multicast and reserved

    private static bool IsBlockedIpv6(IPAddress ip)
    {
        if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.Equals(IPAddress.IPv6Any)) return true;
        var bytes = ip.GetAddressBytes();
        return (bytes[0] & 0xfe) == 0xfc || // RFC 4193 unique-local fc00::/7
               bytes[0] == 0xff;             // multicast
    }

    private static bool TryParseLegacyIpv4(string host, out uint value)
    {
        value = 0;
        var parts = host.Split('.');
        if (parts.Length is < 1 or > 4) return false;
        var numbers = new uint[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            var hex = part.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
            var style = hex ? NumberStyles.AllowHexSpecifier : NumberStyles.None;
            if (!uint.TryParse(hex ? part[2..] : part, style, CultureInfo.InvariantCulture, out numbers[i])) return false;
        }
        value = parts.Length switch
        {
            1 when numbers[0] <= uint.MaxValue => numbers[0],
            2 when numbers[0] <= 255 && numbers[1] <= 0xffffff => (numbers[0] << 24) | numbers[1],
            3 when numbers[0] <= 255 && numbers[1] <= 255 && numbers[2] <= 0xffff => (numbers[0] << 24) | (numbers[1] << 16) | numbers[2],
            4 when numbers.All(x => x <= 255) => (numbers[0] << 24) | (numbers[1] << 16) | (numbers[2] << 8) | numbers[3],
            _ => 0
        };
        return value != 0 || host is "0" or "0.0.0.0";
    }

    [GeneratedRegex(@"(?i)\b[a-z][a-z0-9+.-]*://[^\s""'<>]+")]
    private static partial Regex UrlTokenRegex();
}
