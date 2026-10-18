using System.Net;
using System.Text.Json;

namespace AegisWAF.Tests;

[Collection("gateway")]
public class AuthorizationTests(GatewayFixture fx)
{
    // role -> expected status for each protected operation (security regression matrix)
    [Theory]
    [InlineData("VIEWER", "GET", "/api/overview", 200)]
    [InlineData("VIEWER", "GET", "/api/rules", 200)]
    [InlineData("VIEWER", "GET", "/api/owasp-coverage", 200)]
    [InlineData("VIEWER", "GET", "/api/audit", 403)]
    [InlineData("VIEWER", "GET", "/api/audit/integrity", 403)]
    [InlineData("VIEWER", "POST", "/api/rules/test", 403)]
    [InlineData("VIEWER", "PUT", "/api/ip-policies", 403)]
    [InlineData("VIEWER", "GET", "/api/users", 403)]
    [InlineData("SECURITY_ANALYST", "GET", "/api/audit", 200)]
    [InlineData("SECURITY_ANALYST", "GET", "/api/audit/integrity", 200)]
    [InlineData("SECURITY_ANALYST", "POST", "/api/rules/test", 200)]
    [InlineData("SECURITY_ANALYST", "PUT", "/api/ip-policies", 403)]
    [InlineData("SECURITY_OPERATOR", "PUT", "/api/ip-policies", 204)]
    [InlineData("SECURITY_OPERATOR", "PUT", "/api/rules/sqli-01/enabled", 403)]
    [InlineData("SECURITY_OPERATOR", "GET", "/api/users", 403)]
    [InlineData("SECURITY_ADMIN", "PUT", "/api/rules/sqli-01/enabled", 204)]
    [InlineData("SECURITY_ADMIN", "PUT", "/api/custom-rules/authz-test", 204)]
    [InlineData("SECURITY_ADMIN", "DELETE", "/api/custom-rules/authz-test", 204)]
    [InlineData("SECURITY_ADMIN", "GET", "/api/users", 403)]
    [InlineData("SUPER_ADMIN", "GET", "/api/users", 200)]
    public async Task Role_matrix_is_enforced_server_side(string role, string method, string url, int expected)
    {
        var (api, _, _) = await fx.CreateUserWithRole(role);
        object? body = url switch
        {
            "/api/ip-policies" => new { ip = "203.0.113.7", action = "block", reason = "test" },
            "/api/rules/test" => new { sample = "/index" },
            "/api/rules/sqli-01/enabled" => new { enabled = true },
            "/api/custom-rules/authz-test" => new { name = "t", pattern = "zzz-never", priority = 500, enabled = true },
            _ => null
        };
        var r = await api.Send(new HttpMethod(method), url, body);
        Assert.Equal(expected, (int)r.StatusCode);
    }

    [Fact]
    public async Task Denied_actions_are_audited()
    {
        var (api, name, _) = await fx.CreateUserWithRole("VIEWER");
        await api.Get("/api/users");
        Assert.True(await fx.CountAudit("authz.denied", "denied", name) >= 1);
    }

    [Fact]
    public async Task Mutations_require_a_valid_csrf_token()
    {
        var (api, _, _) = await fx.CreateUserWithRole("SECURITY_OPERATOR");
        var body = new { ip = "203.0.113.8", action = "block", reason = "csrf" };
        Assert.Equal(HttpStatusCode.Forbidden, (await api.Send(HttpMethod.Put, "/api/ip-policies", body, csrf: false)).StatusCode);

        var req = new HttpRequestMessage(HttpMethod.Put, "/api/ip-policies") { Content = System.Net.Http.Json.JsonContent.Create(body) };
        req.Headers.Add("X-CSRF-Token", "not-the-real-token");
        Assert.Equal(HttpStatusCode.Forbidden, (await api.Http.SendAsync(req)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await api.Send(HttpMethod.Put, "/api/ip-policies", body)).StatusCode);
    }

    [Fact]
    public async Task One_sessions_csrf_token_does_not_work_for_another_session()
    {
        var (a, _, _) = await fx.CreateUserWithRole("SECURITY_OPERATOR");
        var (b, _, _) = await fx.CreateUserWithRole("SECURITY_OPERATOR");
        var req = new HttpRequestMessage(HttpMethod.Put, "/api/ip-policies") { Content = System.Net.Http.Json.JsonContent.Create(new { ip = "203.0.113.9", action = "block" }) };
        req.Headers.Add("X-CSRF-Token", b.Csrf);
        Assert.Equal(HttpStatusCode.Forbidden, (await a.Http.SendAsync(req)).StatusCode);
    }

    [Fact]
    public async Task Admin_api_is_unreachable_from_non_local_clients()
    {
        var api = await fx.LoginAdmin();
        foreach (var url in new[] { "/api/overview", "/api/auth/login", "/api/users" })
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("X-Test-Remote", "198.51.100.23");
            Assert.Equal(HttpStatusCode.Forbidden, (await api.Http.SendAsync(req)).StatusCode);
        }
        var gui = new HttpRequestMessage(HttpMethod.Get, "/index.html");
        gui.Headers.Add("X-Test-Remote", "198.51.100.23");
        Assert.Equal(HttpStatusCode.Forbidden, (await api.Http.SendAsync(gui)).StatusCode);
    }

    [Fact]
    public async Task Dashboard_is_served_locally_not_forwarded_to_the_upstream_proxy()
    {
        // Regression: static files used to be shadowed by the YARP catch-all route and proxied upstream (502).
        var api = fx.NewClient();
        var r = await api.Get("/");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("loginForm", await r.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await api.Get("/assets/app.js")).StatusCode);
        Assert.True(r.Headers.Contains("Content-Security-Policy"));
    }

    [Fact]
    public async Task Unknown_api_paths_are_denied_rather_than_proxied_upstream()
    {
        var api = await fx.LoginAdmin();
        Assert.Equal(HttpStatusCode.Forbidden, (await api.Get("/api/definitely-not-an-endpoint")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await fx.NewClient().Get("/api/definitely-not-an-endpoint")).StatusCode);
    }

    [Fact]
    public async Task Owasp_coverage_exposes_all_codes_without_regex_details()
    {
        var api = await fx.LoginAdmin();
        var response = await api.Get("/api/owasp-coverage");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.EnumerateArray().ToArray();
        Assert.Equal(10, items.Length);
        Assert.Equal(Enumerable.Range(1, 10).Select(i => $"A{i:00}"), items.Select(x => x.GetProperty("code").GetString()));
        Assert.DoesNotContain("pattern", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }
}
