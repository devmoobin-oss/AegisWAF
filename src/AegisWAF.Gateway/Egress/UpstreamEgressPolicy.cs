using System.Net;
using System.Net.Sockets;

namespace AegisWAF.Gateway;

internal sealed record UpstreamEgressOptions(
    IReadOnlyCollection<string> AllowedHosts,
    IReadOnlyCollection<int> AllowedPorts,
    IReadOnlyCollection<string> AllowedSchemes,
    bool AllowPrivateExceptions)
{
    public static UpstreamEgressOptions From(IConfiguration cfg) => new(
        cfg.GetSection("Waf:Egress:AllowedHosts").Get<string[]>() ?? [],
        cfg.GetSection("Waf:Egress:AllowedPorts").Get<int[]>() ?? [],
        cfg.GetSection("Waf:Egress:AllowedSchemes").Get<string[]>() ?? [],
        cfg.GetValue("Waf:Egress:AllowPrivateExceptions", false));
}

internal sealed record UpstreamEgressDecision(bool Allowed, string ReasonCode, string? ResolvedAddress = null);

internal interface IUpstreamAddressResolver
{
    Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct);
}

internal sealed class SystemUpstreamAddressResolver : IUpstreamAddressResolver
{
    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct) => Dns.GetHostAddressesAsync(host, ct);
}

internal sealed class UpstreamEgressPolicy(UpstreamEgressOptions options, IUpstreamAddressResolver resolver)
{
    private static readonly HashSet<string> MetadataHosts = new(StringComparer.OrdinalIgnoreCase)
    { "metadata", "metadata.google.internal", "instance-data", "instance-data.ec2.internal", "100.100.100.200" };

    public async Task<UpstreamEgressDecision> CheckAsync(Uri destination, CancellationToken ct = default)
    {
        if (!options.AllowedSchemes.Contains(destination.Scheme, StringComparer.OrdinalIgnoreCase)) return new(false, "scheme_not_allowed");
        if (!options.AllowedPorts.Contains(destination.Port)) return new(false, "port_not_allowed");
        if (!options.AllowedHosts.Contains(destination.Host, StringComparer.OrdinalIgnoreCase)) return new(false, "host_not_allowed");
        if (destination.UserInfo.Length > 0) return new(false, "credentials_not_allowed");

        if (IPAddress.TryParse(destination.Host, out var direct))
            return CheckAddress(direct, options.AllowPrivateExceptions);
        if (MetadataHosts.Contains(destination.Host) || destination.Host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase))
            return new(false, "metadata_host");

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(500));
            var addresses = await resolver.ResolveAsync(destination.Host, timeout.Token).ConfigureAwait(false);
            if (addresses.Length == 0) return new(false, "dns_no_address");
            foreach (var address in addresses)
            {
                var decision = CheckAddress(address, options.AllowPrivateExceptions);
                if (!decision.Allowed) return decision;
            }
            return new(true, "allowed", addresses[0].ToString());
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return new(false, "dns_timeout"); }
        catch (SocketException) { return new(false, "dns_failed"); }
    }

    private static UpstreamEgressDecision CheckAddress(IPAddress address, bool allowPrivate)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return new(false, "loopback_target", address.ToString());
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            var value = (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]);
            var privateTarget = (value & 0xff000000) == 0x0a000000 || (value & 0xfff00000) == 0xac100000 ||
                (value & 0xffff0000) == 0xc0a80000 || (value & 0xffff0000) == 0xa9fe0000 ||
                (value & 0xffc00000) == 0x64400000 || (value & 0xff000000) >= 0xe0000000 || value == 0;
            if (privateTarget && !allowPrivate) return new(false, "private_target", address.ToString());
        }
        else
        {
            var b = address.GetAddressBytes();
            var blocked = address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || b[0] == 0xff || (b[0] & 0xfe) == 0xfc || address.Equals(IPAddress.IPv6Any);
            if (blocked && !allowPrivate) return new(false, "private_target", address.ToString());
        }
        return new(true, "allowed", address.ToString());
    }
}
