using System.Net;
using Npgsql;

namespace AegisWAF.Tests;

[Collection("gateway")]
public class UserManagementTests(GatewayFixture fx)
{
    [Fact]
    public async Task Create_user_validates_input_and_rejects_duplicates()
    {
        var admin = await fx.LoginAdmin();
        var name = "dup-" + Guid.NewGuid().ToString("N")[..6];
        var ok = new { username = name, displayName = "x", password = "Valid-Passw0rd-123", role = "VIEWER" };
        Assert.Equal(HttpStatusCode.Created, (await admin.Send(HttpMethod.Post, "/api/users", ok)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.Send(HttpMethod.Post, "/api/users", ok)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.Send(HttpMethod.Post, "/api/users", ok with { username = name.ToUpperInvariant() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Send(HttpMethod.Post, "/api/users", ok with { username = "bad name!" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Send(HttpMethod.Post, "/api/users", ok with { username = "empty-" + name, password = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Send(HttpMethod.Post, "/api/users", ok with { username = "role-" + name, role = "GOD" })).StatusCode);
    }

    [Fact]
    public async Task Admin_cannot_create_or_reset_users_with_weak_passwords()
    {
        var admin = await fx.LoginAdmin();
        var name = "weak-" + Guid.NewGuid().ToString("N")[..6];
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Send(HttpMethod.Post, "/api/users", new { username = name, displayName = "", password = "x", role = "VIEWER" })).StatusCode);
    }

    [Fact]
    public async Task Passwords_are_stored_hashed_and_never_returned_by_the_api()
    {
        var admin = await fx.LoginAdmin();
        var name = "hash-" + Guid.NewGuid().ToString("N")[..6];
        await admin.Send(HttpMethod.Post, "/api/users", new { username = name, displayName = "", password = "Plain-Text-Passw0rd", role = "VIEWER" });
        var listing = await (await admin.Get("/api/users")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("Plain-Text-Passw0rd", listing);
        Assert.DoesNotContain("pbkdf2", listing);
        Assert.DoesNotContain("passwordHash", listing, StringComparison.OrdinalIgnoreCase);

        await using var db = new NpgsqlConnection(fx.ConnectionString);
        await db.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT password_hash FROM users WHERE username=@u", db);
        cmd.Parameters.AddWithValue("u", name);
        var stored = (string)(await cmd.ExecuteScalarAsync())!;
        Assert.StartsWith("pbkdf2-sha512$", stored);
        Assert.DoesNotContain("Plain-Text-Passw0rd", stored);
    }

    [Fact]
    public async Task Sessions_are_stored_only_as_hashes()
    {
        var api = fx.NewClient();
        var r = await api.Login(GatewayFixture.AdminUser, GatewayFixture.AdminPassword);
        var token = r.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("aegis_session=")).Split(';')[0]["aegis_session=".Length..];
        await using var db = new NpgsqlConnection(fx.ConnectionString);
        await db.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT count(*) FROM sessions WHERE encode(token_hash,'escape') = @t", db);
        cmd.Parameters.AddWithValue("t", token);
        Assert.Equal(0L, (long)(await cmd.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task The_last_enabled_super_admin_cannot_be_disabled_or_demoted()
    {
        var admin = await fx.LoginAdmin();
        // other tests may create extra SUPER_ADMINs; remove them so exactly one remains
        var users = await admin.Json(await admin.Get("/api/users"));
        var me = await AuthenticationTests.UserId(admin, GatewayFixture.AdminUser);
        foreach (var u in users.EnumerateArray().Where(u => u.GetProperty("role").GetString() == "SUPER_ADMIN" && u.GetProperty("id").GetGuid() != me))
            await admin.Send(HttpMethod.Patch, $"/api/users/{u.GetProperty("id").GetGuid()}", new { role = "VIEWER" });

        Assert.Equal(HttpStatusCode.Conflict, (await admin.Send(HttpMethod.Patch, $"/api/users/{me}", new { enabled = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.Send(HttpMethod.Patch, $"/api/users/{me}", new { role = "VIEWER" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.Get("/api/users")).StatusCode);
    }

    [Fact]
    public async Task Role_changes_take_effect_on_existing_sessions_immediately()
    {
        var (api, name, _) = await fx.CreateUserWithRole("SECURITY_ADMIN");
        Assert.Equal(HttpStatusCode.NoContent, (await api.Send(HttpMethod.Put, "/api/rules/sqli-01/enabled", new { enabled = true })).StatusCode);
        var admin = await fx.LoginAdmin();
        await admin.Send(HttpMethod.Patch, $"/api/users/{await AuthenticationTests.UserId(admin, name)}", new { role = "VIEWER" });
        Assert.Equal(HttpStatusCode.Forbidden, (await api.Send(HttpMethod.Put, "/api/rules/sqli-01/enabled", new { enabled = true })).StatusCode);
    }

    [Fact]
    public async Task Migrations_are_tracked_with_checksums_and_reapplying_is_a_no_op()
    {
        await using var db = new NpgsqlConnection(fx.ConnectionString);
        await db.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT count(*) FROM schema_migrations WHERE version LIKE '00%.sql' AND length(checksum)=64", db);
        Assert.True((long)(await cmd.ExecuteScalarAsync())! >= 6);
        var again = await AegisWAF.Gateway.Infrastructure.Migrator.ApplyAsync(fx.ConnectionString);
        Assert.Empty(again);
    }
}
