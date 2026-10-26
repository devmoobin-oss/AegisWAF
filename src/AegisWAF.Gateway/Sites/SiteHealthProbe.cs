using System.Net;
using System.Net.Sockets;
using AegisWAF.Gateway;

namespace AegisWAF.Gateway.Sites;

public sealed record SiteHealthProbeResult(
    string UpstreamId,
    string Address,
    string Health,
    string? Reason,
    DateTimeOffset CheckedAt);

/// <summary>Performs bounded on-demand probes through the same per-site egress guard as YARP.</summary>
public sealed class SiteHealthProbe
{
    public async Task<IReadOnlyList<SiteHealthProbeResult>> ProbeAsync(GatewaySite site, CancellationToken ct)
    {
        var health = site.Health ?? new SiteHealthPolicy();
        var timeout = TimeSpan.FromSeconds(Math.Clamp(health.TimeoutSeconds, 1, 30));
        var policy = new UpstreamEgressPolicy(UpstreamEgressOptions.From(site.Egress), new SystemUpstreamAddressResolver());
        using var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectTimeout = timeout,
            UseProxy = false,
            ConnectCallback = (context, token) => EgressConnectionGuard.ConnectAsync(policy, context.DnsEndPoint, token)
        };
        using var client = new HttpClient(handler) { Timeout = timeout };
        var results = new List<SiteHealthProbeResult>();
        var checkedAt = DateTimeOffset.UtcNow;
        foreach (var upstream in site.Upstreams)
        {
            var probeUri = BuildProbeUri(upstream.Address, health.Path);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, probeUri);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                var healthy = response.IsSuccessStatusCode;
                results.Add(new SiteHealthProbeResult(upstream.Id, upstream.Address.ToString(), healthy ? "healthy" : "unhealthy",
                    healthy ? null : "http_" + (int)response.StatusCode, checkedAt));
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                results.Add(new SiteHealthProbeResult(upstream.Id, upstream.Address.ToString(), "unhealthy", "timeout", checkedAt));
            }
            catch (HttpRequestException ex)
            {
                results.Add(new SiteHealthProbeResult(upstream.Id, upstream.Address.ToString(), "unhealthy", SafeReason(ex), checkedAt));
            }
        }
        return results;
    }

    internal static Uri BuildProbeUri(Uri upstream, string path)
    {
        var normalizedPath = string.IsNullOrWhiteSpace(path) ? "/" : path.StartsWith('/') ? path : "/" + path;
        return new Uri(upstream.GetLeftPart(UriPartial.Authority) + normalizedPath, UriKind.Absolute);
    }

    private static string SafeReason(HttpRequestException ex)
    {
        var detail = FindSocketException(ex) is { } socket
            ? $" ({socket.SocketErrorCode})"
            : "";
        var text = (ex.Message.Trim() + detail).Trim();
        return text.Length <= 128 ? text : text[..128];
    }

    private static SocketException? FindSocketException(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
            if (current is SocketException socket)
                return socket;
        return null;
    }
}
