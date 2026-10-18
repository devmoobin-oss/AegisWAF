using System.Net;
using System.Net.Http.Json;

namespace AegisWAF.Tests;

[Collection("gateway")]
public class AuthenticationTests(GatewayFixture fx)
{
    [Fact]
    public async Task Unauthenticated_requests_to_protected_endpoints_get_401()
    {
        var api = fx.NewClient();
        foreach (var url in new[] { "/api/overview", "/api/rules", "/api/audit", "/api/users", "/api/ip-policies", "/api/auth/session" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await api.Get(url)).StatusCode);
    }

    [Fact]
    public async Task Legacy_admin_token_header_is_not_accepted()
    {
        var api = fx.NewClient();
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/overview");
        req.Headers.Add("X-Aegis-Admin-Token", new string('a', 40));
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Http.SendAsync(req)).StatusCode);
    }

    [Fact]
    public async Task Login_sets_hardened_session_cookie_and_returns_csrf_token()
    {
        var api = fx.NewClient();
        var r = await api.Login(GatewayFixture.AdminUser, GatewayFixture.AdminPassword);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var cookie = r.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("aegis_session="));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        var body = await api.Json(r);
        Assert.Equal("SUPER_ADMIN", body.GetProperty("role").GetString());
        Assert.False(string.IsNullOrEmpty(api.Csrf));
        var raw = await r.Content.ReadAsStringAsync();
        Assert.DoesNotContain("pbkdf2", raw);
        Assert.DoesNotContain(GatewayFixture.AdminPassword, raw);
        Assert.DoesNotContain("hash", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.OK, (await api.Get("/api/overview")).StatusCode);
    }

    [Fact]
    public async Task Unknown_user_and_wrong_password_are_indistinguishable()
    {
        var api = fx.NewClient();
        var a = await api.Login("no-such-user", "Whatever-Passw0rd!");
        var b = await api.Login(GatewayFixture.AdminUser, "Wrong-Passw0rd-123");
        Assert.Equal(HttpStatusCode.Unauthorized, a.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, b.StatusCode);
        Assert.Equal(await a.Content.ReadAsStringAsync(), await b.Content.ReadAsStringAsync());
        Assert.Empty(a.Headers.TryGetValues("Set-Cookie", out var c) ? c : []);
    }

    [Fact]
    public async Task Malformed_login_bodies_never_create_sessions()
    {
        var api = fx.NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Login("", "")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Login(new string('a', 500), new string('b', 500))).StatusCode);
        var r = await api.Http.PostAsJsonAsync("/api/auth/login", new { username = (string?)null, password = (string?)null });
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Account_locks_after_repeated_failures_even_with_the_correct_password()
    {
        var (_, name, password) = await fx.CreateUserWithRole("VIEWER");
        var api = fx.NewClient();
        for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.Unauthorized, (await api.Login(name, "Wrong-Passw0rd-" + i)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Login(name, password)).StatusCode);
        Assert.True(await fx.CountAudit("auth.login", "failure", name) >= 6);
    }

    [Fact]
    public async Task Successful_and_failed_logins_are_audited_with_actor()
    {
        var api = fx.NewClient();
        await api.Login(GatewayFixture.AdminUser, GatewayFixture.AdminPassword);
        await api.Login(GatewayFixture.AdminUser, "Wrong-Passw0rd-999");
        Assert.True(await fx.CountAudit("auth.login", "success", GatewayFixture.AdminUser) >= 1);
        Assert.True(await fx.CountAudit("auth.login", "failure", GatewayFixture.AdminUser) >= 1);
    }

    [Fact]
    public async Task Logout_revokes_the_session_server_side()
    {
        var api = fx.NewClient();
        await api.Login(GatewayFixture.AdminUser, GatewayFixture.AdminPassword);
        // Keep a copy of the cookie to prove the token itself is dead, not merely deleted from the client.
        var cookie = await CaptureCookie(api);
        Assert.Equal(HttpStatusCode.NoContent, (await api.Send(HttpMethod.Post, "/api/auth/logout")).StatusCode);
        var replay = new HttpRequestMessage(HttpMethod.Get, "/api/overview");
        replay.Headers.Add("Cookie", cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, (await fx.NewClient().Http.SendAsync(replay)).StatusCode);
    }

    [Fact]
    public async Task Changing_password_revokes_other_sessions_and_old_password_stops_working()
    {
        var (api, name, password) = await fx.CreateUserWithRole("VIEWER");
        var second = fx.NewClient();
        Assert.Equal(HttpStatusCode.OK, (await second.Login(name, password)).StatusCode);
        var newPassword = "Brand-New-Passw0rd-" + Guid.NewGuid().ToString("N")[..4];

        Assert.Equal(HttpStatusCode.Forbidden, (await api.Send(HttpMethod.Post, "/api/auth/change-password", new { currentPassword = "wrong-current-pass", newPassword })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Send(HttpMethod.Post, "/api/auth/change-password", new { currentPassword = password, newPassword = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await api.Send(HttpMethod.Post, "/api/auth/change-password", new { currentPassword = password, newPassword })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await api.Get("/api/overview")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.Get("/api/overview")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await fx.NewClient().Login(name, password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fx.NewClient().Login(name, newPassword)).StatusCode);
    }

    [Fact]
    public async Task Weak_passwords_are_rejected_when_changing_password()
    {
        var (api, name, password) = await fx.CreateUserWithRole("VIEWER");
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Send(HttpMethod.Post, "/api/auth/change-password", new { currentPassword = password, newPassword = "1" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await fx.NewClient().Login(name, "1")).StatusCode);
    }

    [Fact]
    public async Task Admin_created_accounts_must_change_password_before_doing_anything_else()
    {
        var admin = await fx.LoginAdmin();
        var name = "forced-" + Guid.NewGuid().ToString("N")[..6];
        var temp = "Temporary-Passw0rd-1";
        await admin.Send(HttpMethod.Post, "/api/users", new { username = name, displayName = "", password = temp, role = "VIEWER" });
        var api = fx.NewClient();
        var login = await api.Login(name, temp);
        Assert.True((await api.Json(login)).GetProperty("mustChangePassword").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await api.Get("/api/overview")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.Get("/api/auth/session")).StatusCode);
    }

    [Fact]
    public async Task Disabling_a_user_kills_existing_sessions_and_blocks_login()
    {
        var (api, name, password) = await fx.CreateUserWithRole("VIEWER");
        var admin = await fx.LoginAdmin();
        var id = await UserId(admin, name);
        Assert.Equal(HttpStatusCode.OK, (await api.Get("/api/overview")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.Send(HttpMethod.Patch, $"/api/users/{id}", new { enabled = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Get("/api/overview")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await fx.NewClient().Login(name, password)).StatusCode);
    }

    [Fact]
    public async Task Admin_can_revoke_all_sessions_and_reset_a_password()
    {
        var (api, name, password) = await fx.CreateUserWithRole("SECURITY_ANALYST");
        var admin = await fx.LoginAdmin();
        var id = await UserId(admin, name);
        Assert.Equal(HttpStatusCode.OK, (await admin.Send(HttpMethod.Post, $"/api/users/{id}/revoke-sessions")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Get("/api/overview")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.Send(HttpMethod.Post, $"/api/users/{id}/reset-password", new { newPassword = "Reset-By-Admin-Passw0rd" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await fx.NewClient().Login(name, password)).StatusCode);
        var again = fx.NewClient();
        Assert.True((await again.Json(await again.Login(name, "Reset-By-Admin-Passw0rd"))).GetProperty("mustChangePassword").GetBoolean());
    }

    internal static async Task<Guid> UserId(Api admin, string username)
    {
        var users = await admin.Json(await admin.Get("/api/users"));
        return users.EnumerateArray().Single(u => u.GetProperty("username").GetString() == username).GetProperty("id").GetGuid();
    }

    private static async Task<string> CaptureCookie(Api api)
    {
        // The test server's cookie container is internal to the handler; re-login on a raw client to read Set-Cookie.
        var r = await api.Http.PostAsJsonAsync("/api/auth/login", new { username = GatewayFixture.AdminUser, password = GatewayFixture.AdminPassword });
        var set = r.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("aegis_session="));
        var csrf = (await r.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("csrfToken").GetString();
        typeof(Api).GetProperty(nameof(Api.Csrf))!.SetValue(api, csrf);
        return set.Split(';')[0];
    }
}
