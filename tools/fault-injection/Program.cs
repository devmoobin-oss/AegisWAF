using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using AegisWAF.Gateway;
using Npgsql;

if (!string.Equals(Environment.GetEnvironmentVariable("AEGISWAF_ENABLE_PG_FAULT_INJECTION"), "1", StringComparison.Ordinal))
    return Fail("Set AEGISWAF_ENABLE_PG_FAULT_INJECTION=1 to run this destructive opt-in harness.");

var options = ParseArgs(args);
var statusFile = Required(options, "status-file");
var stopSentinel = Required(options, "stop-sentinel");
var baseConnectionString = Environment.GetEnvironmentVariable("AEGISWAF_TEST_PG")
    ?? "Host=localhost;Port=5432;Username=postgres;Password=postgres;Pooling=false";
var databaseName = "aegiswaf_fault_" + Guid.NewGuid().ToString("N")[..10];
var admin = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = "postgres", Pooling = false, Timeout = 5 };
var database = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = databaseName, Pooling = true, Timeout = 5 };
var created = false;
EmbeddedGateway? gateway = null;
var cancellation = new CancellationTokenSource();
ConsoleCancelEventHandler? cancel = null;

try
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(statusFile))!);
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(stopSentinel))!);
    TryDelete(statusFile);
    TryDelete(stopSentinel);

    await using (var db = new NpgsqlConnection(admin.ConnectionString))
    {
        await db.OpenAsync(cancellation.Token);
        await using var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", db);
        await create.ExecuteNonQueryAsync(cancellation.Token);
    }
    created = true;

    var port = ParsePort(options);
    if (port == 0) port = FreePort();
    gateway = new EmbeddedGateway();
    await gateway.StartAsync(new EmbeddedGatewayOptions(
        new Uri($"http://127.0.0.1:{port}/"),
        database.ConnectionString,
        "Bootstrap-Test-Passw0rd!",
        "http://127.0.0.1:9/",
        AppContext.BaseDirectory), cancellation.Token);

    WriteStatus(statusFile, new
    {
        phase = "runner-ready",
        port,
        database = databaseName,
        pid = Environment.ProcessId,
        recordedAt = DateTimeOffset.UtcNow
    });
    Console.WriteLine($"RUNNER_READY port={port} database={databaseName}");

    cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
    Console.CancelKeyPress += cancel;
    while (!cancellation.IsCancellationRequested && !File.Exists(stopSentinel))
        await Task.Delay(250, cancellation.Token);

    WriteStatus(statusFile, new { phase = "stopping", recordedAt = DateTimeOffset.UtcNow });
    return 0;
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    WriteStatus(statusFile, new { phase = "cancelled", recordedAt = DateTimeOffset.UtcNow });
    return 2;
}
catch (Exception ex)
{
    WriteStatus(statusFile, new { phase = "failed", errorType = ex.GetType().Name, recordedAt = DateTimeOffset.UtcNow });
    Console.Error.WriteLine($"RUNNER_FAILED errorType={ex.GetType().Name}");
    if (string.Equals(Environment.GetEnvironmentVariable("AEGISWAF_FAULT_INJECTION_DEBUG"), "1", StringComparison.Ordinal))
        Console.Error.WriteLine(ex);
    return 1;
}
finally
{
    if (cancel is not null) Console.CancelKeyPress -= cancel;
    cancellation.Cancel();
    if (gateway is not null)
    {
        try { await gateway.StopAsync().WaitAsync(TimeSpan.FromSeconds(15)); }
        catch (TimeoutException)
        {
            Console.Error.WriteLine("RUNNER_STOP_FAILED errorType=TimeoutException");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"RUNNER_STOP_FAILED errorType={ex.GetType().Name}");
            if (string.Equals(Environment.GetEnvironmentVariable("AEGISWAF_FAULT_INJECTION_DEBUG"), "1", StringComparison.Ordinal))
                Console.Error.WriteLine(ex);
        }
    }
    if (created)
    {
        try
        {
            await using var db = new NpgsqlConnection(admin.ConnectionString);
            await db.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", db);
            await drop.ExecuteNonQueryAsync();
            WriteStatus(statusFile, new { phase = "stopped", databaseDropped = true, recordedAt = DateTimeOffset.UtcNow });
        }
        catch (Exception ex)
        {
            WriteStatus(statusFile, new { phase = "cleanup-failed", databaseDropped = false, errorType = ex.GetType().Name, recordedAt = DateTimeOffset.UtcNow });
            Console.Error.WriteLine($"RUNNER_DATABASE_CLEANUP_FAILED errorType={ex.GetType().Name}");
        }
    }
    cancellation.Dispose();
}

static Dictionary<string, string> ParseArgs(string[] args)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length)
            throw new ArgumentException("Arguments must use --name value format");
        result[args[i][2..]] = args[++i];
    }
    return result;
}

static string Required(IReadOnlyDictionary<string, string> options, string name) =>
    options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new ArgumentException($"Missing --{name}");

static int ParsePort(IReadOnlyDictionary<string, string> options) =>
    options.TryGetValue("port", out var value) && int.TryParse(value, out var port) && port is >= 0 and <= 65535
        ? port
        : 0;

static int FreePort()
{
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    return ((IPEndPoint)listener.LocalEndpoint).Port;
}

static void WriteStatus(string path, object value)
{
    try
    {
        var full = Path.GetFullPath(path);
        var temporary = full + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value));
        File.Move(temporary, full, true);
    }
    catch (Exception ex) { Console.Error.WriteLine($"RUNNER_STATUS_WRITE_FAILED errorType={ex.GetType().Name}"); }
}

static void TryDelete(string path)
{
    try { File.Delete(path); }
    catch (IOException) { }
    catch (UnauthorizedAccessException) { }
}

static int Fail(string message)
{
    Console.Error.WriteLine("RUNNER_REFUSED " + message);
    return 78;
}
