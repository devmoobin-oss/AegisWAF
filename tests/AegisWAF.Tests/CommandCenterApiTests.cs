using System.Net;

namespace AegisWAF.Tests;

[Collection("gateway")]
public sealed class CommandCenterApiTests(GatewayFixture fx)
{
    [Fact]
    public async Task Anonymous_cannot_read_command_center_summary()
    {
        var api = fx.NewClient();

        var response = await api.Get("/api/command-center/summary");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Security_analyst_can_read_command_center_summary()
    {
        var (api, _, _) = await fx.CreateUserWithRole("SECURITY_ANALYST");
        {
            var response = await api.Get("/api/command-center/summary");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var json = await api.Json(response);
            Assert.True(json.TryGetProperty("activeConnections", out _));
            Assert.True(json.TryGetProperty("requestsPerMinute", out _));
        }
    }

    [Fact]
    public async Task Viewer_cannot_read_detailed_command_center_traffic()
    {
        var (api, _, _) = await fx.CreateUserWithRole("VIEWER");
        {
            var response = await api.Get("/api/command-center/traffic?limit=100");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Fact]
    public async Task Traffic_endpoint_caps_input_and_never_returns_raw_query_data()
    {
        var (api, _, _) = await fx.CreateUserWithRole("SECURITY_ANALYST");

        var tooLarge = await api.Get("/api/command-center/traffic?limit=501");
        Assert.Equal(HttpStatusCode.BadRequest, tooLarge.StatusCode);

        var invalidDecision = await api.Get("/api/command-center/traffic?decision=not-a-decision");
        Assert.Equal(HttpStatusCode.BadRequest, invalidDecision.StatusCode);

        var empty = await api.Get("/api/command-center/traffic?limit=1&q=token-secret-value");
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        Assert.Equal("no-store", empty.Headers.CacheControl?.ToString());
        var body = await empty.Content.ReadAsStringAsync();
        Assert.DoesNotContain("token-secret-value", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Summary_endpoint_sets_no_store_cache_policy()
    {
        var (api, _, _) = await fx.CreateUserWithRole("SECURITY_ANALYST");

        var response = await api.Get("/api/command-center/summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }
}
