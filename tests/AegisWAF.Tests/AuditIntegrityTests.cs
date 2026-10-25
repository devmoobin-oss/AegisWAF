using System.Net;
using System.Text.Json;
using Npgsql;

namespace AegisWAF.Tests;

[Collection("gateway")]
public sealed class AuditIntegrityTests(GatewayFixture fx)
{
    [Fact]
    public async Task Detects_a_legacy_style_row_inserted_after_integrity_chain_started()
    {
        var api = await fx.LoginAdmin();
        long tamperedId;
        await using (var db = new NpgsqlConnection(fx.ConnectionString))
        {
            await db.OpenAsync();
            await using var insert = new NpgsqlCommand("INSERT INTO waf_audit(occurred_at,action,target,actor,client_ip,outcome,prev_hash,entry_hash) VALUES (now(),'tamper.test','audit',NULL,'local','success',NULL,NULL) RETURNING id", db);
            tamperedId = (long)(await insert.ExecuteScalarAsync())!;
        }

        try
        {
            var response = await api.Get("/api/audit/integrity");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.False(json.RootElement.GetProperty("valid").GetBoolean());
            Assert.Equal(tamperedId, json.RootElement.GetProperty("firstInvalidId").GetInt64());
        }
        finally
        {
            await using var db = new NpgsqlConnection(fx.ConnectionString);
            await db.OpenAsync();
            await using var delete = new NpgsqlCommand("DELETE FROM waf_audit WHERE id=@id", db);
            delete.Parameters.AddWithValue("id", tamperedId);
            await delete.ExecuteNonQueryAsync();
        }
    }
}
