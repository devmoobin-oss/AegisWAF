using System.Net;
using System.Net.Sockets;
using System.Text;
using AegisWAF.Gateway;
using Microsoft.Extensions.Configuration;

namespace AegisWAF.Tests;

/// <summary>
/// Verifies the v0.9 engine additions: deep Unicode (NFKC) normalisation, NoSQL/XXE/SSTI signatures, anomaly scoring
/// (off by default, score-based), and outbound response inspection (leak suppression). Signature/scoring behaviour is
/// tested directly on <see cref="SecurityRules"/>; response inspection is tested end-to-end against a scripted upstream.
/// </summary>
public class EngineUpgradeTests
{
    private static SecurityRules Rules(bool anomaly = false, int threshold = 7)
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Waf:AnomalyScoring:Enabled"] = anomaly ? "true" : "false",
            ["Waf:AnomalyScoring:Threshold"] = threshold.ToString(),
        }).Build();
        return new SecurityRules(cfg);
    }

    // ---- Deep Unicode normalisation (NFKC + invisible-character stripping) ----

    [Fact]
    public void Fullwidth_unicode_brackets_are_folded_to_ascii_and_still_blocked()
    {
        // ＜script＞ in fullwidth code points (U+FF1C / U+FF1E) folds to <script> under NFKC.
        Assert.Equal("XSS", Rules().Match("＜script＞alert(1)＜/script＞"));
    }

    [Fact]
    public void Zero_width_characters_inserted_to_split_a_keyword_are_stripped()
    {
        // "un<ZWSP>ion sel<ZWSP>ect 1" must still be seen as "union select 1".
        Assert.Equal("SQL Injection", Rules().Match("1 un​ion sel​ect 1"));
    }

    [Fact]
    public void Normalizer_produces_a_folded_variant_without_mutating_clean_input()
    {
        var variants = Normalizer.Variants("＜script＞");
        Assert.Contains("<script>", variants);
        // A plain value yields exactly one variant (no spurious duplicates).
        Assert.Single(Normalizer.Variants("hello"));
    }

    [Fact]
    public void Rule_engine_inspects_payload_suffixes_beyond_the_single_value_cap()
    {
        var payload = new string('a', 66_000) + "<script>alert(1)</script>";

        Assert.Equal("XSS", Rules().Match(payload, checkOversize: false));
    }

    [Fact]
    public void Rule_engine_fails_closed_when_inspection_budget_is_expired()
    {
        Assert.Equal("Inspection Timeout", Rules().Match("ordinary input", checkOversize: false, new InspectionBudget(0)));
    }

    [Fact]
    public void Binary_response_scan_detects_high_signal_leaks_without_utf8_decoding()
    {
        var body = new byte[] { 0, 255, (byte)'r', (byte)'o', (byte)'o', (byte)'t', (byte)':', (byte)'x', (byte)':', (byte)'0', (byte)':', (byte)'0', (byte)':', 255 };

        Assert.Equal("Response Leak — passwd/shadow file", new ResponseInspector().InspectBytes(body));
    }

    // ---- New signatures: NoSQL / XXE / SSTI ----

    [Theory]
    [InlineData("{\"$ne\":null}", "NoSQL Injection")]
    [InlineData("user[$gt]=", "NoSQL Injection")]
    [InlineData("{\"user\":{\"$where\":\"1\"}}", "NoSQL Injection")]
    [InlineData("<!DOCTYPE r SYSTEM \"http://evil.example/x.dtd\">", "XXE")]
    [InlineData("{{7*7}}", "Template Injection")]
    [InlineData("{{config.items()}}", "Template Injection")]
    public void New_signature_classes_are_detected(string payload, string category) =>
        Assert.Equal(category, Rules().Match(payload));

    [Theory]
    // These XXE/SSTI payloads overlap earlier, broader rules (file:// → LFI, /etc/passwd → Path, ${…} → shell):
    // they are still blocked, just attributed to the earlier rule. The new rules add the cases the old ones miss.
    [InlineData("<!DOCTYPE r [<!ENTITY x SYSTEM \"file:///etc/passwd\">]>")]
    [InlineData("${7*7}")]
    public void Overlapping_xxe_and_ssti_payloads_are_still_blocked(string payload) =>
        Assert.NotNull(Rules().Match(payload));

    [Theory]
    [InlineData("user[]=admin&pass[]=x")]           // type juggling, no operator token
    [InlineData("<!DOCTYPE html><p>hi</p>")]        // ordinary DOCTYPE, no entity
    [InlineData("{{user.name}}")]                    // normal template echo
    [InlineData("the price is $net of tax")]         // "$ne" not a whole token
    public void Documented_bypasses_for_the_new_classes_pass(string payload) =>
        Assert.Null(Rules().Match(payload));

    // ---- Anomaly scoring (score-based; disabled by default) ----

    [Fact]
    public void Anomaly_scoring_is_off_by_default_so_a_weak_combo_passes()
    {
        // select + /* + < = 3+2+2 = 7 points, but no single signature matches it.
        Assert.Null(Rules(anomaly: false).Match("a' select/* <b"));
    }

    [Fact]
    public void Anomaly_scoring_blocks_when_enough_weak_signals_combine()
    {
        Assert.Equal("Anomaly Score", Rules(anomaly: true).Match("a' select/* <b"));
    }

    [Fact]
    public void Anomaly_scoring_does_not_block_ordinary_traffic_even_when_enabled()
    {
        Assert.Null(Rules(anomaly: true).Match("/search?q=union station departures"));
        Assert.Null(Rules(anomaly: true).Match("/products?id=42&sort=name"));
    }

    [Fact]
    public void Anomaly_rule_is_listed_toggleable_and_counted_only_while_enabled()
    {
        var r = Rules(anomaly: false);
        Assert.True(r.Exists("anomaly-01"));
        Assert.Contains("anomaly-01", System.Text.Json.JsonSerializer.Serialize(r.List())); // visible in the rules grid

        var before = r.Count;
        Assert.True(r.Toggle("anomaly-01", true));
        Assert.Equal(before + 1, r.Count);
        Assert.True(r.Toggle("anomaly-01", false));
        Assert.Equal(before, r.Count);
    }

    // ---- Response inspection (outbound leak suppression) ----

    private sealed class ScriptedUpstream : IDisposable
    {
        private readonly TcpListener _l = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _cts = new();
        private readonly byte[] _response;
        public Uri Url { get; }

        public ScriptedUpstream(string status, string? contentType, string body)
        {
            var bodyBytes = Encoding.UTF8.GetBytes(body);
            var head = new StringBuilder($"HTTP/1.1 {status}\r\n");
            if (contentType is not null) head.Append($"Content-Type: {contentType}\r\n");
            head.Append($"Content-Length: {bodyBytes.Length}\r\nConnection: close\r\n\r\n");
            _response = [.. Encoding.ASCII.GetBytes(head.ToString()), .. bodyBytes];
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

        private async Task Serve(TcpClient c)
        {
            using var client = c;
            try
            {
                var s = client.GetStream(); var buf = new byte[8192]; var got = new StringBuilder();
                while (got.ToString().IndexOf("\r\n\r\n", StringComparison.Ordinal) < 0) { var n = await s.ReadAsync(buf); if (n == 0) return; got.Append(Encoding.Latin1.GetString(buf, 0, n)); }
                await s.WriteAsync(_response);
            }
            catch (Exception) { /* client went away */ }
        }

        public void Dispose() { _cts.Cancel(); _l.Stop(); }
    }

    private static Uri FreeUrl()
    {
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop();
        return new Uri($"http://127.0.0.1:{port}/");
    }

    private static async Task<(int Status, string Body)> GetAsync(Uri baseUrl, string target)
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(baseUrl.Host, baseUrl.Port);
        var stream = tcp.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"GET {target} HTTP/1.1\r\nHost: {baseUrl.Host}\r\nConnection: close\r\n\r\n"));
        using var ms = new MemoryStream();
        var buf = new byte[8192];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        int n;
        while ((n = await stream.ReadAsync(buf, cts.Token)) > 0) ms.Write(buf, 0, n);
        var raw = Encoding.Latin1.GetString(ms.ToArray());
        var status = int.Parse(raw.Split("\r\n")[0].Split(' ')[1]);
        var idx = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        return (status, idx >= 0 ? raw[(idx + 4)..] : "");
    }

    private sealed class Running(EmbeddedGateway gw, ScriptedUpstream up, Uri url) : IAsyncDisposable
    {
        public Uri Url { get; } = url;
        public async ValueTask DisposeAsync() { await gw.StopAsync(); up.Dispose(); }
    }

    [Collection("gateway")]
    public class ResponseInspection(GatewayFixture fx)
    {
        private async Task<Running> StartAsync(ScriptedUpstream up, IReadOnlyDictionary<string, string?>? overrides = null)
        {
            var url = FreeUrl();
            var gw = new EmbeddedGateway();
            await gw.StartAsync(new EmbeddedGatewayOptions(url, fx.ConnectionString, null, up.Url.ToString(), null, overrides));
            return new Running(gw, up, url);
        }

        [Fact]
        public async Task A_passwd_file_leaking_from_the_upstream_is_suppressed_with_502()
        {
            using var up = new ScriptedUpstream("200 OK", "text/plain", "root:x:0:0:root:/root:/bin/bash\ndaemon:x:1:1:");
            await using var run = await StartAsync(up);
            var (status, body) = await GetAsync(run.Url, "/download?f=notes.txt");
            Assert.Equal(502, status);
            Assert.DoesNotContain("root:x:0:0", body); // the leak never reaches the client
        }

        [Fact]
        public async Task A_raw_database_error_leaking_from_the_upstream_is_suppressed()
        {
            using var up = new ScriptedUpstream("200 OK", "application/json",
                "{\"error\":\"Npgsql.PostgresException: 42601: syntax error at or near\"}");
            await using var run = await StartAsync(up);
            var (status, _) = await GetAsync(run.Url, "/api-demo/items");
            Assert.Equal(502, status);
        }

        [Fact]
        public async Task A_clean_html_response_passes_through_unchanged()
        {
            using var up = new ScriptedUpstream("200 OK", "text/html", "<!doctype html><h1>Welcome</h1>");
            await using var run = await StartAsync(up);
            var (status, body) = await GetAsync(run.Url, "/home");
            Assert.Equal(200, status);
            Assert.Contains("Welcome", body);
        }

        [Fact]
        public async Task A_binary_response_is_not_inspected_so_the_same_bytes_pass_through_fail_open()
        {
            // Same passwd content, but declared as a binary type: not buffered/inspected (documented fail-open).
            using var up = new ScriptedUpstream("200 OK", "application/octet-stream", "root:x:0:0:root:/root:/bin/bash");
            await using var run = await StartAsync(up);
            var (status, body) = await GetAsync(run.Url, "/blob");
            Assert.Equal(200, status);
            Assert.Contains("root:x:0:0", body);
        }

        [Fact]
        public async Task A_binary_response_is_scanned_when_binary_inspection_is_enabled()
        {
            using var up = new ScriptedUpstream("200 OK", "application/octet-stream", "root:x:0:0:root:/root:/bin/bash");
            await using var run = await StartAsync(up, new Dictionary<string, string?> { ["Waf:InspectBinaryResponses"] = "true" });
            var (status, body) = await GetAsync(run.Url, "/blob");
            Assert.Equal(502, status);
            Assert.DoesNotContain("root:x:0:0", body);
        }

        [Fact]
        public async Task A_response_larger_than_the_inspection_cap_streams_through_intact()
        {
            // 300 KB > the 256 KB default cap: the buffer overflows and the body is forwarded unbuffered and uncorrupted.
            var payload = new string('a', 300_000);
            using var up = new ScriptedUpstream("200 OK", "text/plain", payload);
            await using var run = await StartAsync(up);
            var (status, body) = await GetAsync(run.Url, "/big");
            Assert.Equal(200, status);
            Assert.Equal(payload.Length, body.Length);
        }

        [Fact]
        public async Task A_block_mode_does_not_return_success_after_response_headers_started_before_overflow()
        {
            using var up = new ScriptedUpstream("200 OK", "text/plain", new string('a', 300_000));
            await using var run = await StartAsync(up, new Dictionary<string, string?>
            {
                ["Waf:ResponseOverflowAction"] = "block",
                ["Waf:MaxResponseInspectBytes"] = "1024"
            });

            var status = 0;
            Exception? failure = null;
            try { (status, _) = await GetAsync(run.Url, "/big-block"); }
            catch (Exception ex) { failure = ex; }

            Assert.True(failure is not null || status != 200,
                $"Fail-closed overflow returned an apparent success status: {status}");
        }
    }
}
