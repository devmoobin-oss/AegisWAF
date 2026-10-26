using AegisWAF.Gateway;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace AegisWAF.Tests;

[Collection("gateway")]
public sealed class SecurityEventPersistenceIntegrationTests(GatewayFixture fx)
{
    [Fact]
    public async Task Runtime_batch_persists_all_events_and_updates_delivery_counters()
    {
        var marker = "batch-runtime-" + Guid.NewGuid().ToString("N")[..8];
        await using var gateway = new EmbeddedGateway();
        var gatewayUrl = new Uri("http://127.0.0.1:" + GetFreePort() + "/");
        await gateway.StartAsync(new EmbeddedGatewayOptions(
            gatewayUrl,
            fx.ConnectionString,
            null,
            "http://127.0.0.1:9/",
            null,
            new Dictionary<string, string?>
            {
                ["AllowedHosts"] = "*",
                ["Waf:Events:StructuredLogging"] = "false",
                ["Waf:Events:BatchSize"] = "3",
                ["Waf:Events:FlushMilliseconds"] = "25"
            }));

        var events = gateway.GetService<SecurityEvents>();
        Assert.True(events.TryWrite(Event(marker + "-1")));
        Assert.True(events.TryWrite(Event(marker + "-2")));
        Assert.True(events.TryWrite(Event(marker + "-3")));

        var count = 0L;
        for (var i = 0; i < 100 && count < 3; i++)
        {
            await Task.Delay(25);
            count = await CountAsync(fx.ConnectionString, marker);
        }

        Assert.Equal(3, count);
        Assert.Equal(3, events.Persisted);
        Assert.Equal(0, events.PersistenceFailures);
        Assert.Equal(0, events.QueueDepth);
        Assert.Equal(3, await CountAsync(fx.ConnectionString, marker));
        await DeleteAsync(fx.ConnectionString, marker);
    }

    [Fact]
    public async Task Event_writer_counts_batch_connection_failure_without_throwing_into_the_host()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = "Host=127.0.0.1;Port=1;Username=invalid;Password=invalid;Database=missing;Timeout=1",
                ["Waf:Events:StructuredLogging"] = "false",
                ["Waf:Events:BatchSize"] = "1",
                ["Waf:Events:FlushMilliseconds"] = "10"
            })
            .Build();
        var events = new SecurityEvents(NullLogger<SecurityEvents>.Instance, configuration);
        using var loggerFactory = LoggerFactory.Create(_ => { });
        var writer = new EventWriter(events, configuration, loggerFactory.CreateLogger<EventWriter>());
        await writer.StartAsync(CancellationToken.None);
        try
        {
            Assert.True(events.TryWrite(Event("failure")));
            for (var i = 0; i < 100 && events.PersistenceFailures == 0; i++) await Task.Delay(25);
            Assert.Equal(1, events.PersistenceFailures);
        }
        finally
        {
            await writer.StopAsync(CancellationToken.None);
        }
    }

    private static SecurityEvent Event(string rule) =>
        new(DateTimeOffset.UtcNow, "127.0.0.1", "GET", "/batch", rule, 403);

    private static async Task<long> CountAsync(string connectionString, string marker)
    {
        await using var db = new NpgsqlConnection(connectionString);
        await db.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM security_events WHERE rule_name LIKE @marker", db);
        command.Parameters.AddWithValue("marker", marker + "%");
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task DeleteAsync(string connectionString, string marker)
    {
        await using var db = new NpgsqlConnection(connectionString);
        await db.OpenAsync();
        await using var command = new NpgsqlCommand("DELETE FROM security_events WHERE rule_name LIKE @marker", db);
        command.Parameters.AddWithValue("marker", marker + "%");
        await command.ExecuteNonQueryAsync();
    }

    private static int GetFreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
