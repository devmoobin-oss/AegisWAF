using System.Net;
using System.Net.Sockets;
using AegisWAF.Gateway;
using Npgsql;

namespace AegisWAF.Tests;

[Collection("gateway")]
public class GatewaySetupTests(GatewayFixture fx)
{
    private DbParams Valid => DbParams.FromConnectionString(fx.ConnectionString);

    [Fact]
    public async Task Existing_migrated_database_is_ready()
    {
        var r = await GatewaySetup.CheckDatabaseAsync(Valid);
        Assert.True(r.Ready);
        Assert.False(r.HasUsers); // the login system was removed; nothing bootstraps a user anymore
    }

    [Fact]
    public async Task Wrong_password_is_reported_as_a_credentials_problem_in_plain_language()
    {
        var r = await GatewaySetup.CheckDatabaseAsync(Valid with { Password = "definitely-wrong" });
        Assert.False(r.ServerReachable);
        Assert.Contains("รหัสผ่าน", r.Message);
    }

    [Fact]
    public async Task Unreachable_server_is_reported_without_throwing()
    {
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start(); var port = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop();
        var r = await GatewaySetup.CheckDatabaseAsync(Valid with { Port = port });
        Assert.False(r.ServerReachable);
        Assert.Contains("PostgreSQL", r.Message);
    }

    [Fact]
    public async Task Missing_database_is_detected_then_created_then_ready()
    {
        var name = "aegis_setup_" + Guid.NewGuid().ToString("N")[..8];
        var p = Valid with { Database = name };
        try
        {
            var before = await GatewaySetup.CheckDatabaseAsync(p);
            Assert.True(before.ServerReachable);
            Assert.False(before.DatabaseExists);
            Assert.False(before.Ready);

            await GatewaySetup.CreateDatabaseAsync(p);
            await GatewaySetup.CreateDatabaseAsync(p); // idempotent
            var after = await GatewaySetup.CheckDatabaseAsync(p);
            Assert.True(after.Ready);
            Assert.False(after.HasUsers); // fresh database, no schema yet
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var db = new NpgsqlConnection((Valid with { Database = "postgres" }).ToConnectionString());
            await db.OpenAsync();
            await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", db);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    [Theory]
    [InlineData("aegiswaf; DROP DATABASE postgres; --")]
    [InlineData("x\"; DROP DATABASE postgres; --")]
    [InlineData("")]
    [InlineData("1starts_with_digit")]
    [InlineData("has space")]
    public async Task Hostile_or_invalid_database_names_are_rejected_before_any_sql_runs(string name)
    {
        Assert.False(GatewaySetup.IsValidDatabaseName(name));
        await Assert.ThrowsAsync<ArgumentException>(() => GatewaySetup.CreateDatabaseAsync(Valid with { Database = name }));
        var check = await GatewaySetup.CheckDatabaseAsync(Valid with { Database = name });
        Assert.False(check.Ready);
    }

    [Fact]
    public async Task Upstream_check_distinguishes_up_down_and_malformed()
    {
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start(); var port = ((IPEndPoint)l.LocalEndpoint).Port;
        var serve = Task.Run(async () =>
        {
            using var c = await l.AcceptTcpClientAsync();
            var s = c.GetStream();
            var buf = new byte[4096]; var got = "";
            while (!got.Contains("\r\n\r\n")) { var n = await s.ReadAsync(buf); if (n == 0) break; got += System.Text.Encoding.ASCII.GetString(buf, 0, n); }
            await s.WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray());
        });
        var up = await GatewaySetup.CheckUpstreamAsync($"http://127.0.0.1:{port}/");
        await serve; l.Stop();
        Assert.True(up.Reachable);

        var down = await GatewaySetup.CheckUpstreamAsync($"http://127.0.0.1:{port}/");
        Assert.False(down.Reachable);
        Assert.False((await GatewaySetup.CheckUpstreamAsync("not a url")).Reachable);
        Assert.False((await GatewaySetup.CheckUpstreamAsync("ftp://example.com/")).Reachable);
    }
}
