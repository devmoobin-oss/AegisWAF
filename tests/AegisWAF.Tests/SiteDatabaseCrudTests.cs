using Npgsql;

namespace AegisWAF.Tests;

[Collection("gateway")]
public sealed class SiteDatabaseCrudTests(GatewayFixture fx)
{
    [Fact]
    public async Task Health_check_endpoint_returns_the_current_site_status()
    {
        var api = await fx.LoginAdmin();
        var id = "health-" + Guid.NewGuid().ToString("N")[..6];
        var add = await api.Send(HttpMethod.Post, "/api/sites", new
        {
            id, hostnames = new[] { "health.example.test" }, pathPrefixes = new[] { "/" },
            upstreamAddress = "https://origin.example.test/", wafPolicy = "default",
            allowedHosts = new[] { "origin.example.test" }, allowedPorts = new[] { 443 },
            allowedSchemes = new[] { "https" }, enabled = true
        });
        add.EnsureSuccessStatusCode();
        var response = await api.Send(HttpMethod.Post, "/api/sites/" + id + "/health");
        response.EnsureSuccessStatusCode();
        var body = await api.Json(response);
        Assert.Equal(id, body.GetProperty("siteId").GetString());
        Assert.True(body.TryGetProperty("health", out _));
    }

    [Fact]
    public async Task Adding_two_sites_persists_both_rows_without_overwriting_the_first()
    {
        var api = await fx.LoginAdmin();
        var firstId = "shop-" + Guid.NewGuid().ToString("N")[..6];
        var secondId = "blog-" + Guid.NewGuid().ToString("N")[..6];

        foreach (var site in new[]
        {
            new { id = firstId, hostnames = new[] { "shop.example.test" } },
            new { id = secondId, hostnames = new[] { "blog.example.test" } }
        })
        {
            var response = await api.Send(HttpMethod.Post, "/api/sites", new
            {
                id = site.id,
                site.hostnames,
                pathPrefixes = new[] { "/" },
                upstreamAddress = "https://origin.example.test/",
                wafPolicy = "default",
                allowedHosts = new[] { "origin.example.test" },
                allowedPorts = new[] { 443 },
                allowedSchemes = new[] { "https" },
                enabled = true
            });
            response.EnsureSuccessStatusCode();
        }

        await using var db = new NpgsqlConnection(fx.ConnectionString);
        await db.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT site_id FROM waf_sites WHERE site_id = ANY(@ids) ORDER BY site_id", db);
        cmd.Parameters.AddWithValue("ids", new[] { firstId, secondId });
        await using var reader = await cmd.ExecuteReaderAsync();
        var ids = new List<string>();
        while (await reader.ReadAsync()) ids.Add(reader.GetString(0));

        Assert.Equal(new[] { firstId, secondId }.OrderBy(x => x), ids);
    }

    [Fact]
    public async Task Update_and_delete_change_only_the_selected_database_row()
    {
        var api = await fx.LoginAdmin();
        var firstId = "edit-" + Guid.NewGuid().ToString("N")[..6];
        var secondId = "keep-" + Guid.NewGuid().ToString("N")[..6];
        async Task Add(string id)
        {
            var response = await api.Send(HttpMethod.Post, "/api/sites", new
            {
                id, hostnames = new[] { id + ".example.test" }, pathPrefixes = new[] { "/" },
                upstreamAddress = "https://origin.example.test/", wafPolicy = "default",
                allowedHosts = new[] { "origin.example.test" }, allowedPorts = new[] { 443 },
                allowedSchemes = new[] { "https" }, enabled = true
            });
            response.EnsureSuccessStatusCode();
        }
        await Add(firstId);
        await Add(secondId);

        var update = await api.Send(HttpMethod.Put, "/api/sites/" + firstId, new
        {
            id = firstId, hostnames = new[] { "changed.example.test" }, pathPrefixes = new[] { "/api" },
            upstreamAddress = "https://origin.example.test/", wafPolicy = "default",
            allowedHosts = new[] { "origin.example.test" }, allowedPorts = new[] { 443 },
            allowedSchemes = new[] { "https" }, enabled = true
        });
        update.EnsureSuccessStatusCode();

        var delete = await api.Send(HttpMethod.Delete, "/api/sites/" + firstId);
        delete.EnsureSuccessStatusCode();

        await using var db = new NpgsqlConnection(fx.ConnectionString);
        await db.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT site_id, hostnames FROM waf_sites WHERE site_id=@id OR site_id=@other ORDER BY site_id", db);
        cmd.Parameters.AddWithValue("id", firstId); cmd.Parameters.AddWithValue("other", secondId);
        await using var reader = await cmd.ExecuteReaderAsync();
        var rows = new List<(string Id, string[] Hosts)>();
        while (await reader.ReadAsync()) rows.Add((reader.GetString(0), (string[])reader[1]));

        var kept = Assert.Single(rows);
        Assert.Equal(secondId, kept.Id);
    }
}
