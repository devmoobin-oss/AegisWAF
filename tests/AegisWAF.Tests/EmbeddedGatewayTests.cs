using System.Net;
using System.Net.Sockets;
using System.Net.Http.Headers;
using AegisWAF.Client;
using AegisWAF.Gateway;
using AegisWAF.Gateway.Sites;
using AegisWAF.Gateway.Traffic;
using Microsoft.Extensions.Configuration;

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
    public async Task Concurrent_start_calls_share_one_embedded_gateway_startup()
    {
        await using var gw = new EmbeddedGateway();
        var gatewayUrl = FreeUrl();
        var options = new EmbeddedGatewayOptions(
            gatewayUrl,
            fx.ConnectionString,
            null,
            "http://127.0.0.1:9/",
            null,
            new Dictionary<string, string?> { ["AllowedHosts"] = "*" });

        var starts = Enumerable.Range(0, 4)
            .Select(_ => Task.Run(() => gw.StartAsync(options)))
            .ToArray();

        await Task.WhenAll(starts);

        Assert.True(gw.Running);
        using var client = new HttpClient { BaseAddress = gatewayUrl };
        using var response = await client.GetAsync("health");
        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Login_base_path_health_probe_and_public_prefix_reach_exact_upstream_paths()
    {
        await using var upstream = await LoopbackUpstream.StartAsync(onlyLogin: true);
        var siteId = "login-probe-" + Guid.NewGuid().ToString("N")[..8];
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:Postgres"] = fx.ConnectionString }).Build();
        var store = new SiteDatabaseStore(config);
        var target = Environment.GetEnvironmentVariable("AEGISWAF_PROBE_UPSTREAM") is { Length: > 0 } liveUrl
            ? new Uri(liveUrl) : new Uri(upstream.Url, "login/");
        var request = new SiteManagementRequest(siteId, [siteId + ".test"], ["/donsak-i"],
            target.ToString(), "default", [target.Host], [target.Port], ["http"]);
        Assert.True(await store.InsertAsync(SiteDatabaseStore.ToGatewaySite(request), "test", default));
        try
        {
            await using var gw = new EmbeddedGateway();
            var gatewayUrl = FreeUrl();
            await gw.StartAsync(new EmbeddedGatewayOptions(gatewayUrl, fx.ConnectionString, null, null,
                ConfigurationOverrides: new Dictionary<string, string?> { ["AllowedHosts"] = "*" }));
            if (target.Port == upstream.Url.Port)
            {
                for (var i = 0; i < 100 && upstream.RequestPaths.IsEmpty; i++) await Task.Delay(50);
                Assert.Contains("/login", upstream.RequestPaths);
            }
            else await Task.Delay(1000);
            using var api = new ApiClient(gatewayUrl);
            var health = await api.CheckSiteHealthAsync(siteId);
            Assert.Equal("unhealthy", health.Health);
            Assert.True(health.Fresh);
            Assert.Equal("active-probe", health.Source);
            using var client = new HttpClient(new HttpClientHandler
                { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Brotli | DecompressionMethods.Deflate }) { BaseAddress = gatewayUrl };
            using var message = new HttpRequestMessage(HttpMethod.Get, "/donsak-i")
                { Headers = { Host = siteId + ".test" } };
            using var response = await client.SendAsync(message);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            if (target.Port == upstream.Url.Port) Assert.Equal("site-upstream", body);
            else Assert.Contains("<html", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(upstream.RequestPaths, x => x.Contains("/login/login") || x.Contains("donsak-i"));
        }
        finally { await store.DeleteAsync(siteId, default); }
    }

    [Fact]
    public async Task Records_allowed_site_request_with_route_and_security_outcome()
    {
        await using var upstream = await LoopbackUpstream.StartAsync();
        var siteId = "telemetry-" + Guid.NewGuid().ToString("N")[..8];
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = fx.ConnectionString })
            .Build();
        var store = new SiteDatabaseStore(config);
        var hostname = siteId + ".test";
        var site = new GatewaySite(siteId, true, [hostname], ["/"],
            [new SiteUpstream("primary", upstream.Url)], "default", new SiteRateLimitPolicy(),
            new SiteEgressPolicy(["127.0.0.1"], [upstream.Url.Port], ["http"]));
        Assert.True(await store.InsertAsync(site, "telemetry-test", default));

        var gatewayUrl = FreeUrl();
        try
        {
            await using var gw = new EmbeddedGateway();
            await gw.StartAsync(new EmbeddedGatewayOptions(gatewayUrl, fx.ConnectionString, null, null, null,
                new Dictionary<string, string?> { ["AllowedHosts"] = "*" }));

            using var client = new HttpClient { BaseAddress = gatewayUrl };
            using var request = new HttpRequestMessage(HttpMethod.Get, "/command-center-probe") { Headers = { Host = hostname } };
            using var response = await client.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();
            var records = gw.Telemetry!.Query(new TrafficQuery(10));
            Assert.True(records.Count == 1, $"status={(int)response.StatusCode}; body={responseBody}; records={records.Count}");
            var record = records.Single();

            Assert.Equal((int)response.StatusCode, record.StatusCode);
            Assert.Equal("/command-center-probe", record.Path);
            Assert.Equal(TrafficDecision.Allowed, record.Decision);
            Assert.Equal(siteId, record.SiteId);
            Assert.Equal("primary", record.UpstreamId);
            Assert.True(!record.Path.Contains('?', StringComparison.Ordinal), $"Unexpected query marker in path [{record.Path}]");
        }
        finally { await store.DeleteAsync(siteId, default); }
    }

    [Fact]
    public async Task Records_blocked_request_with_rule_and_owasp_without_raw_payload()
    {
        await using var upstream = await LoopbackUpstream.StartAsync();
        var siteId = "telemetry-block-" + Guid.NewGuid().ToString("N")[..8];
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = fx.ConnectionString })
            .Build();
        var store = new SiteDatabaseStore(config);
        var hostname = siteId + ".test";
        var site = new GatewaySite(siteId, true, [hostname], ["/"],
            [new SiteUpstream("primary", upstream.Url)], "default", new SiteRateLimitPolicy(),
            new SiteEgressPolicy(["127.0.0.1"], [upstream.Url.Port], ["http"]));
        Assert.True(await store.InsertAsync(site, "telemetry-test", default));

        try
        {
            var gatewayUrl = FreeUrl();
            await using var gw = new EmbeddedGateway();
            await gw.StartAsync(new EmbeddedGatewayOptions(gatewayUrl, fx.ConnectionString, null, null, null,
                new Dictionary<string, string?> { ["AllowedHosts"] = "*" }));

            using var client = new HttpClient { BaseAddress = gatewayUrl };
            using var request = new HttpRequestMessage(HttpMethod.Get, "/blocked?q=1%20union%20select%201");
            request.Headers.Host = hostname;
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            var status = response.StatusCode;
            var responseBody = await response.Content.ReadAsStringAsync();
            response.Dispose();
            for (var i = 0; i < 20 && gw.Telemetry!.Query(new TrafficQuery(10)).Count == 0; i++)
                await Task.Delay(10);
            var records = gw.Telemetry!.Query(new TrafficQuery(10));
            Assert.True(records.Count > 0, $"status={status}; logs={gw.RecentLog}; body={responseBody}");
            var record = Assert.Single(records);

            Assert.Equal(HttpStatusCode.Forbidden, status);
            Assert.Equal(TrafficDecision.Blocked, record.Decision);
            Assert.Equal("SQL Injection", record.Rule);
            Assert.Contains("A03", record.OwaspCodes);
            Assert.DoesNotContain("union", record.Path, StringComparison.OrdinalIgnoreCase);
        }
        finally { await store.DeleteAsync(siteId, default); }
    }

    [Fact]
    public async Task Real_socket_forwards_local_listener_port_and_path_to_site_upstream()
    {
        await using var upstream = await LoopbackUpstream.StartAsync();
        var siteId = "e2e-local-" + Guid.NewGuid().ToString("N")[..8];
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = fx.ConnectionString })
            .Build();
        var store = new SiteDatabaseStore(config);
        var upstreamBase = new Uri(upstream.Url, "a/");
        var hostname = siteId + ".test";
        var site = new GatewaySite(siteId, true, [hostname], ["/"],
            [new SiteUpstream("primary", upstreamBase)], "default", new SiteRateLimitPolicy(),
            new SiteEgressPolicy(["127.0.0.1"], [upstreamBase.Port], ["http"]));
        Assert.True(await store.InsertAsync(site, "e2e-test", default));

        try
        {
            var gatewayUrl = FreeUrl();
            await using var gw = new EmbeddedGateway();
            await gw.StartAsync(new EmbeddedGatewayOptions(gatewayUrl, fx.ConnectionString, null, null, null,
                new Dictionary<string, string?> { ["AllowedHosts"] = "*" }));

            using var client = new HttpClient { BaseAddress = gatewayUrl };
            using var request = new HttpRequestMessage(HttpMethod.Get, "/a/") { Headers = { Host = hostname } };
            using var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            Assert.True(response.StatusCode == HttpStatusCode.OK, body);
            Assert.Equal("site-upstream", body);

            using var api = new ApiClient(gatewayUrl);
            var health = await api.CheckSiteHealthAsync(siteId);
            Assert.False(health.PendingRestart);
            Assert.NotEqual("invalid", health.Health);
            Assert.Single(health.Upstreams);
        }
        finally
        {
            await store.DeleteAsync(siteId, default);
        }
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
    public async Task Real_socket_rejects_protocol_upgrade_before_proxying()
    {
        var url = FreeUrl();
        await using var gw = new EmbeddedGateway();
        await gw.StartAsync(new EmbeddedGatewayOptions(url, fx.ConnectionString, null, "http://127.0.0.1:9/"));

        using var client = new HttpClient { BaseAddress = url };
        using var request = new HttpRequestMessage(HttpMethod.Get, "/payload");
        request.Headers.TryAddWithoutValidation("Connection", "Upgrade");
        request.Headers.TryAddWithoutValidation("Upgrade", "websocket");
        request.Headers.TryAddWithoutValidation("Sec-WebSocket-Version", "13");
        request.Headers.TryAddWithoutValidation("Sec-WebSocket-Key", "dGhlIHNhbXBsZSBub25jZQ==");

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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
    public async Task Real_socket_inspects_binary_request_body_when_binary_inspection_is_enabled()
    {
        await using var upstream = await LoopbackUpstream.StartAsync();
        var url = FreeUrl();
        await using var gw = new EmbeddedGateway();
        await gw.StartAsync(new EmbeddedGatewayOptions(url, fx.ConnectionString, null, upstream.Url.ToString(), null,
            new Dictionary<string, string?> { ["Waf:InspectBinaryRequests"] = "true" }));

        using var client = new HttpClient { BaseAddress = url };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/payload")
        {
            Content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("<script>alert(1)</script>"))
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Real_socket_applies_waf_policy_per_site_without_cross_site_leakage()
    {
        await using var upstream = await LoopbackUpstream.StartAsync();
        var url = FreeUrl();
        await using var gw = new EmbeddedGateway();
        await gw.StartAsync(new EmbeddedGatewayOptions(url, fx.ConnectionString, null, upstream.Url.ToString(), null,
            new Dictionary<string, string?>
            {
                ["AllowedHosts"] = "*",
                ["Waf:Policies:lenient:DisabledRules:0"] = "xss-01",
                ["Sites:alpha:Enabled"] = "true",
                ["Sites:alpha:Hostnames:0"] = "alpha.test",
                ["Sites:alpha:PathPrefixes:0"] = "/",
                ["Sites:alpha:WafPolicy"] = "lenient",
                ["Sites:alpha:Upstreams:one:Address"] = upstream.Url.ToString(),
                ["Sites:beta:Enabled"] = "true",
                ["Sites:beta:Hostnames:0"] = "beta.test",
                ["Sites:beta:PathPrefixes:0"] = "/",
                ["Sites:beta:WafPolicy"] = "default",
                ["Sites:beta:Upstreams:one:Address"] = upstream.Url.ToString()
            }, PreferConfigurationSites: true));

        using var client = new HttpClient { BaseAddress = url };
        const string payload = "%3Cscript%3Ealert(1)%3C%2Fscript%3E";
        using var alpha = new HttpRequestMessage(HttpMethod.Get, "/payload?probe=" + payload);
        alpha.Headers.Host = "alpha.test";
        using var beta = new HttpRequestMessage(HttpMethod.Get, "/payload?probe=" + payload);
        beta.Headers.Host = "beta.test";

        using var alphaResponse = await client.SendAsync(alpha);
        using var betaResponse = await client.SendAsync(beta);

        Assert.True(alphaResponse.StatusCode == HttpStatusCode.OK, await alphaResponse.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden, betaResponse.StatusCode);
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
        await gw.StartAsync(new EmbeddedGatewayOptions(url, fx.ConnectionString, null, null, null, config, PreferConfigurationSites: true));

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
        await gw.StartAsync(new EmbeddedGatewayOptions(url, fx.ConnectionString, null, null, null, config, PreferConfigurationSites: true));

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
    public async Task Site_crud_activates_routes_immediately_without_gateway_restart()
    {
        await using var firstUpstream = await LoopbackUpstream.StartAsync(System.Text.Encoding.UTF8.GetBytes("first-upstream"));
        await using var secondUpstream = await LoopbackUpstream.StartAsync(System.Text.Encoding.UTF8.GetBytes("second-upstream"));
        var url = FreeUrl();
        var siteId = "live-crud-" + Guid.NewGuid().ToString("N")[..8];
        var firstHost = siteId + ".first.test";
        var secondHost = siteId + ".second.test";
        await using var gw = new EmbeddedGateway();
        await gw.StartAsync(new EmbeddedGatewayOptions(url, fx.ConnectionString, null, null, null,
            new Dictionary<string, string?> { ["AllowedHosts"] = "*" }));

        using var api = new ApiClient(url);
        try
        {
            await api.AddSiteAsync(new SiteCreateDto(siteId, [firstHost], ["/"], firstUpstream.Url.ToString(), "default",
                ["127.0.0.1"], [firstUpstream.Url.Port], ["http"]));

            using var client = new HttpClient { BaseAddress = url };
            using var addedRequest = new HttpRequestMessage(HttpMethod.Get, "/created") { Headers = { Host = firstHost } };
            using var addedResponse = await client.SendAsync(addedRequest);
            Assert.Equal(HttpStatusCode.OK, addedResponse.StatusCode);
            Assert.Equal("first-upstream", await addedResponse.Content.ReadAsStringAsync());

            var current = (await api.GetSitesAsync()).Single(x => x.SiteId == siteId);
            await api.UpdateSiteAsync(siteId, new SiteCreateDto(siteId, [secondHost], ["/"], secondUpstream.Url.ToString(), "default",
                ["127.0.0.1"], [secondUpstream.Url.Port], ["http"], ExpectedVersion: current.Version));

            using var updatedRequest = new HttpRequestMessage(HttpMethod.Get, "/updated") { Headers = { Host = secondHost } };
            using var updatedResponse = await client.SendAsync(updatedRequest);
            Assert.Equal(HttpStatusCode.OK, updatedResponse.StatusCode);
            Assert.Equal("second-upstream", await updatedResponse.Content.ReadAsStringAsync());
            using var oldHostRequest = new HttpRequestMessage(HttpMethod.Get, "/old") { Headers = { Host = firstHost } };
            using var oldHostResponse = await client.SendAsync(oldHostRequest);
            Assert.Equal((HttpStatusCode)421, oldHostResponse.StatusCode);

            var updated = (await api.GetSitesAsync()).Single(x => x.SiteId == siteId);
            await api.DeleteSiteAsync(siteId, updated.Version);
            using var deletedRequest = new HttpRequestMessage(HttpMethod.Get, "/deleted") { Headers = { Host = secondHost } };
            using var deletedResponse = await client.SendAsync(deletedRequest);
            Assert.Equal((HttpStatusCode)421, deletedResponse.StatusCode);
        }
        finally
        {
            try
            {
                var remaining = (await api.GetSitesAsync()).FirstOrDefault(x => x.SiteId == siteId);
                if (remaining is not null) await api.DeleteSiteAsync(siteId, remaining.Version);
            }
            catch { /* The assertions above own the failure; cleanup is best effort. */ }
        }
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
        await gw.StartAsync(new EmbeddedGatewayOptions(url, fx.ConnectionString, null, null, null, config, PreferConfigurationSites: true));

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
        await gw.StartAsync(new EmbeddedGatewayOptions(url, fx.ConnectionString, null, null, null, config, PreferConfigurationSites: true));

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
        private readonly bool _onlyLogin;
        public System.Collections.Concurrent.ConcurrentQueue<string> RequestPaths { get; } = new();

        private LoopbackUpstream(TcpListener listener, byte[]? body, bool onlyLogin)
        {
            _onlyLogin = onlyLogin;
            _listener = listener;
            _body = body ?? System.Text.Encoding.UTF8.GetBytes("site-upstream");
            Url = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/");
            _loop = AcceptAsync();
        }

        public Uri Url { get; }

        public static Task<LoopbackUpstream> StartAsync(byte[]? body = null, bool onlyLogin = false)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return Task.FromResult(new LoopbackUpstream(listener, body, onlyLogin));
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

        private async Task RespondAsync(TcpClient client, byte[] body)
        {
            using var owned = client;
            await using var stream = owned.GetStream();
            using var reader = new StreamReader(stream, System.Text.Encoding.ASCII, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
            var requestLine = await reader.ReadLineAsync();
            var requestPath = requestLine?.Split(' ').ElementAtOrDefault(1) ?? "";
            RequestPaths.Enqueue(requestPath);
            while ((await reader.ReadLineAsync()) is { Length: > 0 }) { }
            var status = !_onlyLogin || requestPath == "/login" || requestPath == "/login/" ? "200 OK" : "404 Not Found";
            var head = System.Text.Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Length: {body.Length}\r\nConnection: close\r\nContent-Type: text/plain\r\n\r\n");
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
