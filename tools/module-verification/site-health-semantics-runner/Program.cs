using System.Text.Json;
using System.Net;
using System.Net.Sockets;
using System.Text;
using AegisWAF.Gateway.Sites;

var evidencePath = args.Length > 0 ? args[0] : null;
var results = new List<object>();
var now = DateTimeOffset.UtcNow;

Run("same-batch-counts-once", () =>
{
    var trend = SiteHealthTrendCalculator.Calculate([
        new("primary", "unhealthy", "timeout", now),
        new("backup", "unhealthy", "timeout", now),
        new("primary", "healthy", null, now.AddMinutes(-1)),
        new("backup", "healthy", null, now.AddMinutes(-1))
    ], 20, 2);
    Require(trend.ConsecutiveUnhealthy == 1 && trend.AlertState == "degraded", $"streak={trend.ConsecutiveUnhealthy}; state={trend.AlertState}");
});

Run("three-complete-failed-batches-critical", () =>
{
    var observations = Enumerable.Range(0, 3).SelectMany(i => new[]
    {
        new SiteHealthObservation("primary", "unhealthy", "timeout", now.AddSeconds(-i * 10)),
        new SiteHealthObservation("backup", "unhealthy", "timeout", now.AddSeconds(-i * 10))
    }).ToArray();
    var trend = SiteHealthTrendCalculator.Calculate(observations, 20, 2);
    Require(trend.ConsecutiveUnhealthy == 3 && trend.AlertState == "critical", $"streak={trend.ConsecutiveUnhealthy}; state={trend.AlertState}");
});

Run("partial-failure-degraded", () =>
{
    var observations = Enumerable.Range(0, 3).SelectMany(i => new[]
    {
        new SiteHealthObservation("primary", "unhealthy", "timeout", now.AddSeconds(-i * 10)),
        new SiteHealthObservation("backup", "healthy", null, now.AddSeconds(-i * 10))
    }).ToArray();
    var trend = SiteHealthTrendCalculator.Calculate(observations, 20, 2);
    Require(trend.ConsecutiveUnhealthy == 0 && trend.AlertState == "degraded", $"streak={trend.ConsecutiveUnhealthy}; state={trend.AlertState}");
});

Run("incomplete-failure-not-critical", () =>
{
    var observations = Enumerable.Range(0, 3)
        .Select(i => new SiteHealthObservation("primary", "unhealthy", "timeout", now.AddSeconds(-i * 10)))
        .ToArray();
    var trend = SiteHealthTrendCalculator.Calculate(observations, 20, 2);
    Require(trend.ConsecutiveUnhealthy == 0 && trend.AlertState == "degraded", $"streak={trend.ConsecutiveUnhealthy}; state={trend.AlertState}");
});

Run("timestamp-skew-normalized", () =>
{
    var batchBase = new DateTimeOffset(now.UtcDateTime.Ticks - now.UtcDateTime.Ticks % TimeSpan.TicksPerSecond, TimeSpan.Zero);
    var trend = SiteHealthTrendCalculator.Calculate([
        new("primary", "unhealthy", "timeout", batchBase),
        new("backup", "unhealthy", "timeout", batchBase.AddMilliseconds(400))
    ], 20, 2);
    Require(trend.ConsecutiveUnhealthy == 1 && trend.AlertState == "degraded", $"streak={trend.ConsecutiveUnhealthy}; state={trend.AlertState}");
});

Run("complete-recovery-retains-history", () =>
{
    var observations = new[]
    {
        new SiteHealthObservation("primary", "healthy", null, now),
        new SiteHealthObservation("backup", "healthy", null, now),
        new SiteHealthObservation("primary", "unhealthy", "timeout", now.AddSeconds(-10)),
        new SiteHealthObservation("backup", "unhealthy", "timeout", now.AddSeconds(-10))
    };
    var trend = SiteHealthTrendCalculator.Calculate(observations, 20, 2);
    Require(trend.AlertState == "degraded" && trend.RecoveryState == "recovered", $"state={trend.AlertState}; recovery={trend.RecoveryState}");
});

await RunAsync("probe-batch-shares-checked-at", async () =>
{
    await using var server = await LoopbackHttpServer.StartAsync();
    var site = new GatewaySite(
        "probe-batch", true, ["probe-batch.example.test"], ["/"],
        [new SiteUpstream("primary", server.Url), new SiteUpstream("backup", server.Url)], "default",
        new SiteRateLimitPolicy(), new SiteEgressPolicy(["127.0.0.1"], [server.Port], ["http"], true),
        new SiteHealthPolicy("/ready", 10, 1, 30));
    var probe = await new SiteHealthProbe().ProbeAsync(site, CancellationToken.None);
    Require(probe.Count == 2 && probe.All(x => x.Health == "healthy") && probe[0].CheckedAt == probe[1].CheckedAt,
        $"count={probe.Count}; health={string.Join(',', probe.Select(x => x.Health))}; reasons={string.Join(',', probe.Select(x => x.Reason ?? "none"))}");
});

var success = results.All(x => x.GetType().GetProperty("result")?.GetValue(x)?.ToString() == "passed");
Console.WriteLine(success
    ? $"SITE_HEALTH_SEMANTICS_PASS scenarios={results.Count}"
    : $"SITE_HEALTH_SEMANTICS_FAIL scenarios={results.Count}");
if (!string.IsNullOrWhiteSpace(evidencePath))
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(evidencePath))!);
    File.WriteAllText(evidencePath, JsonSerializer.Serialize(new
    {
        module = 50,
        success,
        progress = new { before = 0, after = success ? 100 : 90, target = 100 },
        scenarios = results,
        recordedAt = DateTimeOffset.UtcNow
    }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
}

void Run(string name, Action action)
{
    try
    {
        action();
        results.Add(new { name, result = "passed" });
    }
    catch (Exception ex)
    {
        results.Add(new { name, result = "failed", errorType = ex.GetType().Name, message = ex.Message });
        Console.Error.WriteLine($"SITE_HEALTH_SEMANTICS_FAIL scenario={name} errorType={ex.GetType().Name}");
        Environment.ExitCode = 1;
    }
}

async Task RunAsync(string name, Func<Task> action)
{
    try
    {
        await action();
        results.Add(new { name, result = "passed" });
    }
    catch (Exception ex)
    {
        results.Add(new { name, result = "failed", errorType = ex.GetType().Name, message = ex.Message });
        Console.Error.WriteLine($"SITE_HEALTH_SEMANTICS_FAIL scenario={name} errorType={ex.GetType().Name}");
        Environment.ExitCode = 1;
    }
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class LoopbackHttpServer : IAsyncDisposable
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
                var buffer = new byte[4096];
                _ = await stream.ReadAsync(buffer, _stop.Token);
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
