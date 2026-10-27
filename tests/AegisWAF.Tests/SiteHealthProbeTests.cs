using AegisWAF.Gateway.Sites;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace AegisWAF.Tests;

public sealed class SiteHealthProbeTests
{
    [Fact]
    public async Task Probe_uses_the_site_egress_guard_for_private_destinations()
    {
        var site = new GatewaySite(
            "probe", true, ["probe.example.test"], ["/"],
            [new SiteUpstream("primary", new Uri("https://127.0.0.1/"))], "default",
            new SiteRateLimitPolicy(), new SiteEgressPolicy(["127.0.0.1"], [443], ["https"]),
            new SiteHealthPolicy("/ready", 10, 1, 30));

        var result = Assert.Single(await new SiteHealthProbe().ProbeAsync(site, default));

        Assert.Equal("unhealthy", result.Health);
        Assert.True(result.Reason == "request_failed" || result.Reason!.StartsWith("socket_", StringComparison.Ordinal),
            $"reason was not a safe category: {result.Reason}");
        Assert.DoesNotContain("127.0.0.1", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Probe_uri_anchors_health_path_at_the_upstream_authority()
    {
        var uri = SiteHealthProbe.BuildProbeUri(new Uri("https://origin.example.test/base/"), "/ready");

        Assert.Equal("https://origin.example.test/ready", uri.ToString());
    }

    [Fact]
    public void Probe_uri_defaults_empty_path_to_root_without_query_or_fragment()
    {
        var uri = SiteHealthProbe.BuildProbeUri(new Uri("https://origin.example.test/base/"), "");

        Assert.Equal("https://origin.example.test/", uri.ToString());
    }

    [Fact]
    public async Task Probe_returns_healthy_for_a_live_upstream_with_an_explicit_test_exception()
    {
        await using var server = await LoopbackHttpServer.StartAsync();
        var port = server.Port;
        var site = new GatewaySite(
            "probe-live", true, ["probe-live.example.test"], ["/"],
            [new SiteUpstream("primary", server.Url)], "default",
            new SiteRateLimitPolicy(), new SiteEgressPolicy(["127.0.0.1"], [port], ["http"], true),
            new SiteHealthPolicy("/ready", 10, 1, 30));

        var result = Assert.Single(await new SiteHealthProbe().ProbeAsync(site, default));

        Assert.True(result.Health == "healthy", $"health={result.Health}; reason={result.Reason}; address={result.Address}");
        Assert.Null(result.Reason);
    }

    [Fact]
    public async Task Probe_assigns_one_checked_at_timestamp_to_all_upstreams_in_a_batch()
    {
        await using var server = await LoopbackHttpServer.StartAsync();
        var site = new GatewaySite(
            "probe-batch", true, ["probe-batch.example.test"], ["/"],
            [new SiteUpstream("primary", server.Url), new SiteUpstream("backup", server.Url)], "default",
            new SiteRateLimitPolicy(), new SiteEgressPolicy(["127.0.0.1"], [server.Port], ["http"], true),
            new SiteHealthPolicy("/ready", 10, 1, 30));

        var results = await new SiteHealthProbe().ProbeAsync(site, default);

        Assert.Equal(2, results.Count);
        Assert.All(results, result => Assert.Equal("healthy", result.Health));
        Assert.Equal(results[0].CheckedAt, results[1].CheckedAt);
    }

    [Fact]
    public async Task Probe_reports_a_bounded_failure_reason_for_an_unavailable_connection()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var site = new GatewaySite(
            "probe-refused", true, ["probe-refused.example.test"], ["/"],
            [new SiteUpstream("primary", new Uri($"http://127.0.0.1:{port}/"))], "default",
            new SiteRateLimitPolicy(), new SiteEgressPolicy(["127.0.0.1"], [port], ["http"], true),
            new SiteHealthPolicy("/ready", 10, 1, 30));

        var result = Assert.Single(await new SiteHealthProbe().ProbeAsync(site, default));

        Assert.Equal("unhealthy", result.Health);
        Assert.InRange(result.Reason!.Length, 1, 128);
        Assert.StartsWith("socket_", result.Reason, StringComparison.Ordinal);
        Assert.True(Enum.TryParse<SocketError>(result.Reason["socket_".Length..], out _),
            $"reason was not a safe socket code: {result.Reason}");
    }

    [Fact]
    public async Task Probe_preserves_caller_cancellation_instead_of_returning_a_false_health_result()
    {
        var site = new GatewaySite(
            "probe-cancelled", true, ["probe-cancelled.example.test"], ["/"],
            [new SiteUpstream("primary", new Uri("http://127.0.0.1:1/"))], "default",
            new SiteRateLimitPolicy(), new SiteEgressPolicy(["127.0.0.1"], [1], ["http"], true),
            new SiteHealthPolicy("/ready", 10, 1, 30));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new SiteHealthProbe().ProbeAsync(site, cancellation.Token));
    }

    [Fact]
    public void Failure_reason_does_not_echo_exception_details()
    {
        var exception = new HttpRequestException(
            "https://user:secret@internal-db.example/password=secret: connection failed");

        var reason = SiteHealthProbe.SafeFailureReason(exception);

        Assert.Equal("request_failed", reason);
        Assert.DoesNotContain("internal-db", reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", reason, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class LoopbackHttpServer : IAsyncDisposable
    {
        private static readonly byte[] Response = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok");
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _serve;

        private LoopbackHttpServer()
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Url = new Uri($"http://127.0.0.1:{Port}/");
            _serve = ServeAsync();
        }

        public int Port { get; }
        public Uri Url { get; }

        public static Task<LoopbackHttpServer> StartAsync() => Task.FromResult(new LoopbackHttpServer());

        private async Task ServeAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    await using var stream = client.GetStream();
                    var request = new byte[4096];
                    var received = new StringBuilder();
                    while (!received.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
                    {
                        var count = await stream.ReadAsync(request, _stop.Token);
                        if (count == 0) break;
                        received.Append(Encoding.ASCII.GetString(request, 0, count));
                    }
                    await stream.WriteAsync(Response, _stop.Token);
                    await stream.FlushAsync(_stop.Token);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            catch (ObjectDisposedException) when (_stop.IsCancellationRequested) { }
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            _listener.Stop();
            try { await _serve; }
            catch (SocketException) when (_stop.IsCancellationRequested) { }
            catch (ObjectDisposedException) when (_stop.IsCancellationRequested) { }
            _stop.Dispose();
        }
    }
}
