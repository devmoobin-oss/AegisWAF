using Npgsql;

namespace AegisWAF.Tests;

[Collection("gateway")]
public sealed class SecurityEventRetentionIntegrationTests(GatewayFixture fx)
{
    [Fact]
    public async Task Cleanup_deletes_expired_events_but_keeps_recent_events()
    {
        var marker = "retention-" + Guid.NewGuid().ToString("N");
        await using (var db = new NpgsqlConnection(fx.ConnectionString))
        {
            await db.OpenAsync();
            await using var insert = new NpgsqlCommand("INSERT INTO security_events (occurred_at, client_ip, http_method, request_path, rule_name, status_code) VALUES (now() - interval '2 hours', '127.0.0.1', 'GET', '/', @old, 403), (now(), '127.0.0.1', 'GET', '/', @recent, 403)", db);
            insert.Parameters.AddWithValue("old", marker + "-old");
            insert.Parameters.AddWithValue("recent", marker + "-recent");
            await insert.ExecuteNonQueryAsync();
        }

        try
        {
            var deleted = await SecurityEventRetentionWorker.DeleteExpiredAsync(fx.ConnectionString, TimeSpan.FromHours(1), default);
            Assert.True(deleted >= 1);

            await using var db = new NpgsqlConnection(fx.ConnectionString);
            await db.OpenAsync();
            await using var count = new NpgsqlCommand("SELECT rule_name FROM security_events WHERE rule_name LIKE @marker ORDER BY rule_name", db);
            count.Parameters.AddWithValue("marker", marker + "%");
            var rows = new List<string>();
            await using var reader = await count.ExecuteReaderAsync();
            while (await reader.ReadAsync()) rows.Add(reader.GetString(0));
            Assert.Equal([marker + "-recent"], rows);
        }
        finally
        {
            await using var db = new NpgsqlConnection(fx.ConnectionString);
            await db.OpenAsync();
            await using var cleanup = new NpgsqlCommand("DELETE FROM security_events WHERE rule_name LIKE @marker", db);
            cleanup.Parameters.AddWithValue("marker", marker + "%");
            await cleanup.ExecuteNonQueryAsync();
        }
    }
}
