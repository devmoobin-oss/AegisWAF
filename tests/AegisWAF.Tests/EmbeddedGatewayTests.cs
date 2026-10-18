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

    private sealed class LoopbackUpstream : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _loop;

        private LoopbackUpstream(TcpListener listener)
        {
            _listener = listener;
            Url = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/");
            _loop = AcceptAsync();
        }

        public Uri Url { get; }

        public static Task<LoopbackUpstream> StartAsync()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return Task.FromResult(new LoopbackUpstream(listener));
        }

        private async Task AcceptAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    _ = RespondAsync(client);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            catch (ObjectDisposedException) when (_stop.IsCancellationRequested) { }
        }

        private static async Task RespondAsync(TcpClient client)
        {
            using var owned = client;
            await using var stream = owned.GetStream();
            using var reader = new StreamReader(stream, System.Text.Encoding.ASCII, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
            while ((await reader.ReadLineAsync()) is { Length: > 0 }) { }
            var body = System.Text.Encoding.UTF8.GetBytes("site-upstream");
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
