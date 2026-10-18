using System.Net;
using System.Net.Sockets;
using System.Net.Http.Headers;
using AegisWAF.Client;
using AegisWAF.Gateway;

namespace AegisWAF.Tests;

/// <summary>The desktop app hosts the gateway in-process; verify that path end to end over a real socket.</summary>
[Collection("gateway")]
public class EmbeddedGatewayTests(GatewayFixture fx)
{
    private static Uri FreeUrl()
    {
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop();
        return new Uri($"http://127.0.0.1:{port}/");
    }

    [Fact]
    public async Task Starts_in_process_serves_the_admin_api_and_stops_cleanly()
    {
        var url = FreeUrl();
        await using var gw = new EmbeddedGateway();
        await gw.StartAsync(new EmbeddedGatewayOptions(url, fx.ConnectionString, null, "http://127.0.0.1:9/"));
        Assert.True(gw.Running);

        using var api = new ApiClient(url);
        Assert.True(await api.IsHealthyAsync());
        var s = await api.LoginAsync(GatewayFixture.AdminUser, GatewayFixture.AdminPassword); // user already bootstrapped in the shared DB
        Assert.Equal("SUPER_ADMIN", s.Role);
        Assert.NotNull(await api.GetOverviewAsync());

        await gw.StopAsync();
        Assert.False(gw.Running);
        Assert.False(await api.IsHealthyAsync());
    }

    [Fact]
    public async Task Bad_database_credentials_fail_startup_with_an_exception_and_leave_nothing_running()
    {
        var url = FreeUrl();
        await using var gw = new EmbeddedGateway();
        await Assert.ThrowsAnyAsync<Exception>(() => gw.StartAsync(new EmbeddedGatewayOptions(url,
            "Host=localhost;Port=5432;Database=aegiswaf_does_not_exist;Username=postgres;Password=wrong-password;Timeout=3", null, null)));
        Assert.False(gw.Running);
        using var api = new ApiClient(url);
        Assert.False(await api.IsHealthyAsync());
    }

    [Fact]
    public async Task Port_already_in_use_fails_startup()
    {
        var url = FreeUrl();
        using var blocker = new TcpListener(IPAddress.Loopback, url.Port); blocker.Start();
        await using var gw = new EmbeddedGateway();
        await Assert.ThrowsAnyAsync<Exception>(() => gw.StartAsync(new EmbeddedGatewayOptions(url, fx.ConnectionString, null, null)));
        Assert.False(gw.Running);
    }

    [Fact]
    public async Task Real_socket_rejects_malformed_utf8_before_proxying()
    {
        var url = FreeUrl();
        await using var gw = new EmbeddedGateway();
        await gw.StartAsync(new EmbeddedGatewayOptions(url, fx.ConnectionString, null, "http://127.0.0.1:9/"));

        using var client = new HttpClient { BaseAddress = url };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/payload")
        {
            Content = new ByteArrayContent([0xC3, 0x28])
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Real_socket_rejects_duplicate_json_keys_before_proxying()
    {
        var url = FreeUrl();
        await using var gw = new EmbeddedGateway();
        await gw.StartAsync(new EmbeddedGatewayOptions(url, fx.ConnectionString, null, "http://127.0.0.1:9/"));

        using var client = new HttpClient { BaseAddress = url };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/payload")
        {
            Content = new StringContent("{\"role\":\"user\",\"role\":\"admin\"}", System.Text.Encoding.UTF8, "application/json")
        };

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Real_socket_blocks_malformed_utf8_in_textual_upstream_response()
    {
        await using var upstream = await LoopbackUpstream.StartAsync([0xC3, 0x28]);
        var url = FreeUrl();
        var config = new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "*",
            ["Sites:response:Enabled"] = "true",
            ["Sites:response:Hostnames:0"] = "response.test",
            ["Sites:response:PathPrefixes:0"] = "/",
            ["Sites:response:Upstreams:one:Address"] = upstream.Url.ToString()
        };
        await using var gw = new EmbeddedGateway();
        await gw.StartAsync(new EmbeddedGatewayOptions(url, fx.ConnectionString, null, null, null, config));

        using var client = new HttpClient { BaseAddress = url };
        using var request = new HttpRequestMessage(HttpMethod.Get, "/response") { Headers = { Host = "response.test" } };
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task Real_socket_routes_two_sites_and_rejects_unknown_host()
    {
        await using var upstream = await LoopbackUpstream.StartAsync();
        var url = FreeUrl();
        var config = new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "*",
            ["Sites:alpha:Enabled"] = "true",
            ["Sites:alpha:Hostnames:0"] = "alpha.test",
            ["Sites:alpha:PathPrefixes:0"] = "/",
            ["Sites:alpha:Upstreams:one:Address"] = upstream.Url.ToString(),
            ["Sites:beta:Enabled"] = "true",
            ["Sites:beta:Hostnames:0"] = "beta.test",
            ["Sites:beta:PathPrefixes:0"] = "/",
            ["Sites:beta:Upstreams:one:Address"] = upstream.Url.ToString()
        };
        await using var gw = new EmbeddedGateway();
        await gw.StartAsync(new EmbeddedGatewayOptions(url, fx.ConnectionString, null, null, null, config));

        using var client = new HttpClient { BaseAddress = url };
        using var alpha = new HttpRequestMessage(HttpMethod.Get, "/alpha") { Headers = { Host = "alpha.test" } };
        using var beta = new HttpRequestMessage(HttpMethod.Get, "/beta") { Headers = { Host = "beta.test" } };
        using var unknown = new HttpRequestMessage(HttpMethod.Get, "/unknown") { Headers = { Host = "unknown.test" } };

        using var alphaResponse = await client.SendAsync(alpha);
        using var betaResponse = await client.SendAsync(beta);
        using var unknownResponse = await client.SendAsync(unknown);

        Assert.Equal(HttpStatusCode.OK, alphaResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, betaResponse.StatusCode);
        Assert.Equal((HttpStatusCode)421, unknownResponse.StatusCode);
    }

    [Fact]
    public async Task Real_socket_keeps_concurrent_site_routing_isolated()
    {
        await using var upstream = await LoopbackUpstream.StartAsync();
        var url = FreeUrl();
        var config = new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "*",
            ["Sites:alpha:Hostnames:0"] = "alpha.test",
            ["Sites:alpha:Upstreams:one:Address"] = upstream.Url.ToString(),
            ["Sites:beta:Hostnames:0"] = "beta.test",
            ["Sites:beta:Upstreams:one:Address"] = upstream.Url.ToString()
        };
        await using var gw = new EmbeddedGateway();
        await gw.StartAsync(new EmbeddedGatewayOptions(url, fx.ConnectionString, null, null, null, config));

        using var client = new HttpClient { BaseAddress = url };
        var requests = Enumerable.Range(0, 30).Select(async index =>
        {
            var host = index % 3 == 0 ? "unknown.test" : index % 2 == 0 ? "alpha.test" : "beta.test";
            using var request = new HttpRequestMessage(HttpMethod.Get, "/concurrent/" + index);
            request.Headers.Host = host;
            using var response = await client.SendAsync(request);
            return (host, response.StatusCode);
        });

        var results = await Task.WhenAll(requests);

        Assert.All(results.Where(x => x.host is "alpha.test" or "beta.test"), x => Assert.Equal(HttpStatusCode.OK, x.StatusCode));
        Assert.All(results.Where(x => x.host == "unknown.test"), x => Assert.Equal((HttpStatusCode)421, x.StatusCode));
    }

    [Fact]
    public async Task Real_socket_passive_health_removes_failed_destinations_and_keeps_healthy_failover()
    {
        await using var upstream = await LoopbackUpstream.StartAsync();
        var badPorts = Enumerable.Range(0, 2).Select(_ => FreeUrl().Port).ToArray();
        var url = FreeUrl();
        var config = new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "*",
            ["Sites:failover:Hostnames:0"] = "failover.test",
            ["Sites:failover:Health:IntervalSeconds"] = "5",
            ["Sites:failover:Health:TimeoutSeconds"] = "1",
            ["Sites:failover:Health:PassiveReactivationSeconds"] = "5",
            ["Sites:failover:Upstreams:healthy:Address"] = upstream.Url.ToString(),
            ["Sites:failover:Upstreams:bad-a:Address"] = $"http://127.0.0.1:{badPorts[0]}/",
            ["Sites:failover:Upstreams:bad-b:Address"] = $"http://127.0.0.1:{badPorts[1]}/"
        };
        await using var gw = new EmbeddedGateway();
        await gw.StartAsync(new EmbeddedGatewayOptions(url, fx.ConnectionString, null, null, null, config));

        using var client = new HttpClient { BaseAddress = url, Timeout = TimeSpan.FromSeconds(5) };
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 50; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/failover/" + i);
            request.Headers.Host = "failover.test";
            using var response = await client.SendAsync(request);
            statuses.Add(response.StatusCode);
            if (statuses.Count >= 10 && statuses.TakeLast(5).All(x => x == HttpStatusCode.OK)) break;
        }

        Assert.Contains(HttpStatusCode.OK, statuses);
        Assert.True(statuses.TakeLast(5).All(x => x == HttpStatusCode.OK),
            $"Expected stable healthy failover, observed: {string.Join(',', statuses)}");
    }

    private sealed class LoopbackUpstream : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _loop;
        private readonly byte[] _body;

        private LoopbackUpstream(TcpListener listener, byte[]? body)
        {
            _listener = listener;
            _body = body ?? System.Text.Encoding.UTF8.GetBytes("site-upstream");
            Url = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/");
            _loop = AcceptAsync();
        }

        public Uri Url { get; }

        public static Task<LoopbackUpstream> StartAsync(byte[]? body = null)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return Task.FromResult(new LoopbackUpstream(listener, body));
        }

        private async Task AcceptAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    _ = RespondAsync(client, _body);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            catch (ObjectDisposedException) when (_stop.IsCancellationRequested) { }
        }

        private static async Task RespondAsync(TcpClient client, byte[] body)
        {
            using var owned = client;
            await using var stream = owned.GetStream();
            using var reader = new StreamReader(stream, System.Text.Encoding.ASCII, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
            while ((await reader.ReadLineAsync()) is { Length: > 0 }) { }
            var head = System.Text.Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nConnection: close\r\nContent-Type: text/plain\r\n\r\n");
            await stream.WriteAsync(head);
            await stream.WriteAsync(body);
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            _listener.Stop();
            try { await _loop; } catch (OperationCanceledException) { }
            _stop.Dispose();
        }
    }
}
