using System.Net;
using System.Net.Sockets;
using System.Text;
using AegisWAF.Client;
using AegisWAF.Gateway;

namespace AegisWAF.Tests;

/// <summary>
/// Every claim in <see cref="ProtectionCatalog"/> (shown in the app and documented in docs/PROTECTION.md) is executed here against a real
/// gateway over a real socket, with the request bytes exactly as written, so the "what we protect against" page cannot overstate or drift.
/// </summary>
[Collection("gateway")]
public class CatalogVerificationTests(GatewayFixture fx)
{
    private static Uri FreeUrl()
    {
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop();
        return new Uri($"http://127.0.0.1:{port}/");
    }

    /// <summary>Minimal upstream that answers every request with 200 immediately (connecting to a closed port costs ~2 s on Windows).</summary>
    private sealed class FakeUpstream : IDisposable
    {
        private readonly TcpListener _l = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _cts = new();
        private static readonly byte[] Response = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok");
        public Uri Url { get; }

        public FakeUpstream()
        {
            _l.Start();
            Url = new Uri($"http://127.0.0.1:{((IPEndPoint)_l.LocalEndpoint).Port}/");
            _ = Task.Run(async () =>
            {
                while (!_cts.IsCancellationRequested)
                {
                    TcpClient c;
                    try { c = await _l.AcceptTcpClientAsync(_cts.Token); } catch (Exception) { return; }
                    _ = Task.Run(() => Serve(c));
                }
            });
        }

        private static async Task Serve(TcpClient c)
        {
            using var client = c;
            try
            {
                var s = client.GetStream(); var buf = new byte[8192]; var got = new StringBuilder();
                int headEnd;
                while ((headEnd = got.ToString().IndexOf("\r\n\r\n", StringComparison.Ordinal)) < 0) { var n = await s.ReadAsync(buf); if (n == 0) return; got.Append(Encoding.Latin1.GetString(buf, 0, n)); }
                // Drain the request body before answering, otherwise closing with unread data resets the connection.
                var text = got.ToString();
                var bodySoFar = text.Length - (headEnd + 4);
                var m = System.Text.RegularExpressions.Regex.Match(text[..headEnd], @"(?im)^content-length:\s*(\d+)");
                if (m.Success) { var need = int.Parse(m.Groups[1].Value) - bodySoFar; while (need > 0) { var n = await s.ReadAsync(buf); if (n == 0) break; need -= n; } }
                else if (text[..headEnd].Contains("chunked", StringComparison.OrdinalIgnoreCase))
                    while (!got.ToString().EndsWith("0\r\n\r\n", StringComparison.Ordinal)) { var n = await s.ReadAsync(buf); if (n == 0) break; got.Append(Encoding.Latin1.GetString(buf, 0, n)); }
                await s.WriteAsync(Response);
            }
            catch (Exception) { /* client went away */ }
        }

        public void Dispose() { _cts.Cancel(); _l.Stop(); }
    }

    private sealed class Running(EmbeddedGateway gw, FakeUpstream up, Uri url) : IAsyncDisposable
    {
        public Uri Url { get; } = url;
        public async ValueTask DisposeAsync() { await gw.StopAsync(); up.Dispose(); }
    }

    private async Task<Running> StartAsync()
    {
        var up = new FakeUpstream();
        var url = FreeUrl();
        var gw = new EmbeddedGateway();
        await gw.StartAsync(new EmbeddedGatewayOptions(
            url,
            fx.ConnectionString,
            null,
            up.Url.ToString(),
            ConfigurationOverrides: new Dictionary<string, string?> { ["AllowedHosts"] = "*" },
            PreferConfigurationSites: true));
        return new Running(gw, up, url);
    }

    /// <summary>Sends the request exactly as specified (no URL normalisation by an HTTP client) and returns the status code.</summary>
    private static async Task<int> SendRawAsync(Uri baseUrl, ProbeRequest r)
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(baseUrl.Host, baseUrl.Port);
        var stream = tcp.GetStream();
        var head = new StringBuilder($"{r.Method} {r.Target} HTTP/1.1\r\nHost: {baseUrl.Host}\r\nConnection: close\r\n");
        if (r.Headers is not null) foreach (var (k, v) in r.Headers) head.Append($"{k}: {v}\r\n");
        if (r.ContentType is not null) head.Append($"Content-Type: {r.ContentType}\r\n");
        var body = r.Body is null ? null : Encoding.UTF8.GetBytes(r.Body);
        if (body is not null) head.Append(r.Chunked ? "Transfer-Encoding: chunked\r\n" : $"Content-Length: {body.Length}\r\n");
        head.Append("\r\n");
        await stream.WriteAsync(Encoding.UTF8.GetBytes(head.ToString()));
        if (body is not null)
        {
            if (r.Chunked)
            {
                await stream.WriteAsync(Encoding.ASCII.GetBytes($"{body.Length:X}\r\n"));
                await stream.WriteAsync(body);
                await stream.WriteAsync(Encoding.ASCII.GetBytes("\r\n0\r\n\r\n"));
            }
            else await stream.WriteAsync(body);
        }
        var buf = new byte[256];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var n = await stream.ReadAsync(buf, cts.Token);
        var line = Encoding.ASCII.GetString(buf, 0, n).Split("\r\n")[0];
        return int.Parse(line.Split(' ')[1]);
    }

    [Fact]
    public async Task Every_catalog_example_behaves_exactly_as_the_app_says_on_a_real_gateway_and_in_the_sandbox()
    {
        await using var run = await StartAsync();
        var url = run.Url;
        using var api = new ApiClient(url);
        var failures = new List<string>();
        foreach (var item in ProtectionCatalog.Items)
            foreach (var ex in item.Examples)
            {
                var status = await SendRawAsync(url, ex.Request);
                var blocked = status == 403;
                if (status == 429) failures.Add($"[{item.Id}] {ex.Label}: hit the rate limit (test bug)");
                else if (blocked != ex.ExpectBlocked) failures.Add($"[{item.Id}] {ex.Label}: expected {(ex.ExpectBlocked ? "BLOCKED (403)" : "passed through (200)")}, gateway answered {status}");
                else if (!blocked && status != 200) failures.Add($"[{item.Id}] {ex.Label}: passed but the upstream answered {status} instead of 200");

                if (ex.SandboxText is not null && ex.SandboxText.Length <= 8192)
                {
                    var sb = await api.TestRuleAsync(ex.SandboxText);
                    if (sb.Matched != ex.ExpectBlocked) failures.Add($"[{item.Id}] {ex.Label}: sandbox says matched={sb.Matched}, catalog says blocked={ex.ExpectBlocked}");
                }
            }
        Assert.True(failures.Count == 0, Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Catalog_covers_all_three_levels_and_every_gap_or_partial_claim_is_demonstrated()
    {
        Assert.Contains(ProtectionCatalog.Items, i => i.Coverage == Coverage.Covered);
        Assert.Contains(ProtectionCatalog.Items, i => i.Coverage == Coverage.Partial);
        Assert.Contains(ProtectionCatalog.Items, i => i.Coverage == Coverage.NotCovered);
        Assert.Equal(ProtectionCatalog.Items.Count, ProtectionCatalog.Items.Select(i => i.Id).Distinct().Count());
        // A "not covered" claim must be demonstrated by at least one request that passes, except the general disclaimer item.
        foreach (var i in ProtectionCatalog.Items.Where(i => i.Coverage == Coverage.NotCovered && i.Id != "other"))
            Assert.Contains(i.Examples, e => !e.ExpectBlocked);
        // A "partial" content rule must show both what is caught and what slips through.
        foreach (var i in ProtectionCatalog.Items.Where(i => i.Coverage == Coverage.Partial && i.Examples.Count > 0))
        {
            Assert.Contains(i.Examples, e => e.ExpectBlocked);
            Assert.Contains(i.Examples, e => !e.ExpectBlocked);
        }
    }

    [Fact]
    public async Task Ip_block_blocks_everything_and_ip_allow_does_not_bypass_inspection()
    {
        await using var run = await StartAsync();
        var url = run.Url;
        using var api = new ApiClient(url);
        var attack = new ProbeRequest("GET", "/s?q=sleep(5)");
        var harmless = new ProbeRequest("GET", "/products?id=42");
        try
        {
            await api.SaveIpPolicyAsync("127.0.0.1", "allow", "test");
            Assert.Equal(403, await SendRawAsync(url, attack)); // allow only exempts the address from the blocklist
            await api.SaveIpPolicyAsync("127.0.0.1", "block", "test");
            Assert.Equal(403, await SendRawAsync(url, harmless)); // "block" rejects even harmless requests
            Assert.Equal(403, await SendRawAsync(url, attack));
        }
        finally { await api.DeleteIpPolicyAsync("127.0.0.1"); } // never leave the shared test database blocking loopback
        Assert.Equal(200, await SendRawAsync(url, harmless));
        Assert.Equal(403, await SendRawAsync(url, attack));
    }

    [Fact]
    public async Task One_ip_is_limited_to_300_requests_per_minute_with_http_429()
    {
        await using var run = await StartAsync();
        var req = new ProbeRequest("GET", "/rate-probe");
        var first429 = -1;
        // fixed 60-second windows: if the minute rolls over mid-test the counter restarts, so allow up to two windows
        for (var i = 1; i <= 640 && first429 < 0; i++) if (await SendRawAsync(run.Url, req) == 429) first429 = i;
        Assert.True(first429 > 0, "no 429 within 640 requests");
        Assert.InRange(first429, 2, 340); // never limited right away; limited soon after 300 within one window
    }

    [Fact]
    public void Protection_documentation_lists_every_catalog_item()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AegisWAF.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var doc = File.ReadAllText(Path.Combine(dir!.FullName, "docs", "PROTECTION.md"), Encoding.UTF8);
        foreach (var item in ProtectionCatalog.Items) Assert.Contains(item.Title, doc);
    }
}
