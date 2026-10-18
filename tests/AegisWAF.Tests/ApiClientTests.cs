using AegisWAF.Client;

namespace AegisWAF.Tests;

/// <summary>The desktop app talks to the gateway only through ApiClient, so it is exercised against the real pipeline here.</summary>
[Collection("gateway")]
public class ApiClientTests(GatewayFixture fx)
{
    private ApiClient NewClient() => new(fx.NewClient().Http);

    [Fact]
    public async Task Login_populates_session_and_overview_deserialises()
    {
        using var c = NewClient();
        var s = await c.LoginAsync(GatewayFixture.AdminUser, GatewayFixture.AdminPassword);
        Assert.Equal("SUPER_ADMIN", s.Role);
        Assert.Equal(5, s.Level);
        Assert.NotNull(c.Session);
        var o = await c.GetOverviewAsync();
        Assert.True(o.ActiveRules >= 3);
        Assert.NotNull(o.Timeline);
        Assert.Equal(24, o.Timeline.Count);
    }

    [Fact]
    public async Task Bad_credentials_throw_ApiException_401_without_a_session()
    {
        using var c = NewClient();
        var ex = await Assert.ThrowsAsync<ApiException>(() => c.LoginAsync(GatewayFixture.AdminUser, "wrong-password-123"));
        Assert.Equal(401, ex.Status);
        Assert.Null(c.Session);
    }

    [Fact]
    public async Task Mutations_send_the_csrf_token_automatically()
    {
        using var c = NewClient();
        await c.LoginAsync(GatewayFixture.AdminUser, GatewayFixture.AdminPassword);
        await c.SaveIpPolicyAsync("203.0.113.55", "block", "client test");
        Assert.Contains(await c.GetIpPoliciesAsync(), p => p.Ip == "203.0.113.55" && p.Action == "block");
        await c.DeleteIpPolicyAsync("203.0.113.55");
        Assert.DoesNotContain(await c.GetIpPoliciesAsync(), p => p.Ip == "203.0.113.55");
    }

    [Fact]
    public async Task Custom_rules_and_rule_sandbox_round_trip()
    {
        using var c = NewClient();
        await c.LoginAsync(GatewayFixture.AdminUser, GatewayFixture.AdminPassword);
        await c.SaveCustomRuleAsync(new CustomRuleDto("client-rule", "Client rule", "zzz-block-me", 50, true));
        Assert.Contains(await c.GetCustomRulesAsync(), r => r.Id == "client-rule");
        var hit = await c.TestRuleAsync("/x?q=zzz-block-me");
        Assert.True(hit.Matched);
        Assert.True(hit.DryRun);
        Assert.False((await c.TestRuleAsync("/harmless")).Matched);
        await c.DeleteCustomRuleAsync("client-rule");
        Assert.DoesNotContain(await c.GetCustomRulesAsync(), r => r.Id == "client-rule");
    }

    [Fact]
    public async Task Insufficient_role_surfaces_as_403_with_server_message()
    {
        var (api, _, _) = await fx.CreateUserWithRole("VIEWER");
        using var c = new ApiClient(api.Http);
        await c.RefreshSessionAsync();
        var ex = await Assert.ThrowsAsync<ApiException>(() => c.GetUsersAsync());
        Assert.Equal(403, ex.Status);
        Assert.Equal("Insufficient role", ex.Message);
    }

    [Fact]
    public async Task User_management_calls_work_for_super_admin()
    {
        using var c = NewClient();
        await c.LoginAsync(GatewayFixture.AdminUser, GatewayFixture.AdminPassword);
        var name = "cli-" + Guid.NewGuid().ToString("N")[..6];
        await c.CreateUserAsync(name, "Client user", "Valid-Passw0rd-123", "VIEWER");
        var user = (await c.GetUsersAsync()).Single(u => u.Username == name);
        Assert.Equal("VIEWER", user.Role);
        await c.UpdateUserAsync(user.Id, "SECURITY_ANALYST", null);
        await c.ResetPasswordAsync(user.Id, "Another-Passw0rd-456");
        await c.RevokeSessionsAsync(user.Id);
        await c.UpdateUserAsync(user.Id, null, false);
        Assert.Equal("ปิดใช้งาน", (await c.GetUsersAsync()).Single(u => u.Username == name).Status);
        var dup = await Assert.ThrowsAsync<ApiException>(() => c.CreateUserAsync(name, "", "Valid-Passw0rd-123", "VIEWER"));
        Assert.Equal(409, dup.Status);
    }

    [Fact]
    public async Task Logout_clears_session_and_later_calls_raise_SessionExpired_only_when_logged_in()
    {
        using var c = NewClient();
        await c.LoginAsync(GatewayFixture.AdminUser, GatewayFixture.AdminPassword);
        var expired = 0;
        c.SessionExpired += () => expired++;
        await c.LogoutAsync();
        Assert.Null(c.Session);
        await Assert.ThrowsAsync<ApiException>(() => c.GetOverviewAsync());
        Assert.Equal(0, expired); // already logged out locally: not an "expiry"

        // Server-side revocation while the client believes it is logged in => event fires
        using var d = NewClient();
        await d.LoginAsync(GatewayFixture.AdminUser, GatewayFixture.AdminPassword);
        d.SessionExpired += () => expired++;
        var admin = await fx.LoginAdmin();
        var users = await admin.Json(await admin.Get("/api/users"));
        var id = users.EnumerateArray().Single(u => u.GetProperty("username").GetString() == GatewayFixture.AdminUser).GetProperty("id").GetGuid();
        (await admin.Send(HttpMethod.Post, $"/api/users/{id}/revoke-sessions")).EnsureSuccessStatusCode();
        await Assert.ThrowsAsync<ApiException>(() => d.GetOverviewAsync());
        Assert.Equal(1, expired);
    }

    [Fact]
    public async Task Forced_password_change_flow_is_visible_to_the_client()
    {
        var admin = await fx.LoginAdmin();
        var name = "cf-" + Guid.NewGuid().ToString("N")[..6];
        await admin.Send(HttpMethod.Post, "/api/users", new { username = name, displayName = "", password = "Temporary-Passw0rd-1", role = "VIEWER" });
        using var c = NewClient();
        var s = await c.LoginAsync(name, "Temporary-Passw0rd-1");
        Assert.True(s.MustChangePassword);
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => c.GetOverviewAsync())).Status);
        await c.ChangePasswordAsync("Temporary-Passw0rd-1", "Chosen-By-User-Passw0rd");
        Assert.NotNull(await c.GetOverviewAsync());
    }

    [Fact]
    public async Task Self_test_is_blocked_when_the_sqli_rule_is_on_and_not_blocked_when_it_is_off()
    {
        using var c = NewClient();
        await c.LoginAsync(GatewayFixture.AdminUser, GatewayFixture.AdminPassword);
        await c.SetRuleEnabledAsync("sqli-01", true);
        var on = await c.RunSelfTestAsync();
        Assert.True(on.Blocked);
        Assert.Equal(403, on.Status);
        try
        {
            await c.SetRuleEnabledAsync("sqli-01", false);
            var off = await c.RunSelfTestAsync();
            Assert.False(off.Blocked); // reaches the (absent) upstream => 502, which proves it was not blocked
        }
        finally { await c.SetRuleEnabledAsync("sqli-01", true); }
        Assert.True((await c.RunSelfTestAsync()).Blocked);
    }

    [Fact]
    public async Task Unreachable_server_is_reported_as_status_zero()
    {
        using var c = new ApiClient(new Uri("http://127.0.0.1:1/"));
        var ex = await Assert.ThrowsAsync<ApiException>(() => c.LoginAsync("a", "b"));
        Assert.Equal(0, ex.Status);
        Assert.False(await c.IsHealthyAsync());
    }
}
