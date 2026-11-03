using System.Net;
using System.Net.Sockets;
using AegisWAF.Gateway;
using Microsoft.Extensions.Configuration;

namespace AegisWAF.Gateway.Sites;

public sealed record SiteHealthProbeResult(
    string UpstreamId,
    string Address,
    string Health,
    string? Reason,
    DateTimeOffset CheckedAt);

/// <summary>Performs bounded on-demand probes through the same per-site egress guard as YARP.</summary>
public sealed class SiteHealthProbe : IDisposable
{
    internal const int MaxConcurrentUpstreams = 8;
    internal const int MaxConcurrentBatches = 16;
    private const int MaxConfiguredConcurrentBatches = 128;
    private readonly SemaphoreSlim _batchConcurrency;

    public SiteHealthProbe(IConfiguration? configuration = null)
    {
        var maxBatches = Math.Clamp(
            configuration?.GetValue("Waf:Sites:HealthProbeMaxConcurrentBatches", MaxConcurrentBatches) ?? MaxConcurrentBatches,
            1,
            MaxConfiguredConcurrentBatches);
        _batchConcurrency = new SemaphoreSlim(maxBatches);
    }

    public async Task<IReadOnlyList<SiteHealthProbeResult>> ProbeAsync(GatewaySite site, CancellationToken ct)
    {
        await _batchConcurrency.WaitAsync(ct);
        try
        {
            return await ProbeCoreAsync(site, ct);
        }
        finally
        {
            _batchConcurrency.Release();
        }
    }

    public void Dispose() => _batchConcurrency.Dispose();

    private static async Task<IReadOnlyList<SiteHealthProbeResult>> ProbeCoreAsync(GatewaySite site, CancellationToken ct)
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
        var checkedAt = DateTimeOffset.UtcNow;

        using var concurrency = new SemaphoreSlim(MaxConcurrentUpstreams);
        var probes = site.Upstreams.Select(async upstream =>
        {
            await concurrency.WaitAsync(ct);
            try
            {
                var probeUri = BuildProbeUri(upstream.Address, health.Path);
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, probeUri);
                    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                    var healthy = response.IsSuccessStatusCode;
                    return new SiteHealthProbeResult(upstream.Id, SafeDisplayAddress(upstream.Address), healthy ? "healthy" : "unhealthy",
                        healthy ? null : "http_" + (int)response.StatusCode, checkedAt);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    return new SiteHealthProbeResult(upstream.Id, SafeDisplayAddress(upstream.Address), "unhealthy", "timeout", checkedAt);
                }
                catch (HttpRequestException ex)
                {
                    return new SiteHealthProbeResult(upstream.Id, SafeDisplayAddress(upstream.Address), "unhealthy", SafeFailureReason(ex), checkedAt);
                }
            }
            finally
            {
                concurrency.Release();
            }
        });

        return await Task.WhenAll(probes);
    }

    internal static Uri BuildProbeUri(Uri upstream, string path)
    {
        var normalizedPath = string.IsNullOrWhiteSpace(path) ? "/" : path.StartsWith('/') ? path : "/" + path;
        return new Uri($"{upstream.Scheme}://{upstream.Authority}{normalizedPath}", UriKind.Absolute);
    }

    /// <summary>Returns the route address without user-info, query, or fragment data.</summary>
    internal static string SafeDisplayAddress(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return $"{address.Scheme}://{address.Authority}{address.AbsolutePath}";
    }

    internal static string SafeFailureReason(HttpRequestException ex)
    {
        return FindSocketException(ex) is { } socket
            ? $"socket_{socket.SocketErrorCode}"
            : "request_failed";
    }

    private static SocketException? FindSocketException(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
            if (current is SocketException socket)
                return socket;
        return null;
    }
}
