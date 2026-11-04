using System.Net;
using System.Text;
using AegisWAF.Client;

namespace AegisWAF.Tests;

public sealed class ApiClientProblemDetailsTests
{
    [Fact]
    public async Task ApiClient_surfaces_safe_rfc7807_title_and_detail_instead_of_only_http_status()
    {
        using var client = NewClient("""
            {"title":"Site database unavailable","status":503,"detail":"The Site configuration could not be saved.","code":"site_database_unavailable","traceId":"redacted"}
            """);

        var error = await Assert.ThrowsAsync<ApiException>(() => client.GetOverviewAsync());

        Assert.Equal(503, error.Status);
        Assert.Equal("site_database_unavailable", error.Code);
        Assert.Equal("Site database unavailable: The Site configuration could not be saved.", error.Message);
        Assert.DoesNotContain("traceId", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApiClient_preserves_a_safe_machine_code_for_localized_client_recovery_copy()
    {
        using var client = NewClient("""
            {"title":"Site database unavailable","status":503,"detail":"ตรวจสอบ PostgreSQL","code":"site_database_unavailable"}
            """);

        var error = await Assert.ThrowsAsync<ApiException>(() => client.GetOverviewAsync());

        Assert.Equal(503, error.Status);
        Assert.Equal("site_database_unavailable", error.Code);
    }

    [Fact]
    public async Task Legacy_error_field_remains_the_primary_server_message()
    {
        using var client = NewClient("""
            {"error":"Site hostname conflicts with an active Site","title":"Conflict","detail":"internal detail"}
            """, HttpStatusCode.Conflict);

        var error = await Assert.ThrowsAsync<ApiException>(() => client.GetOverviewAsync());

        Assert.Equal(409, error.Status);
        Assert.Equal("Site hostname conflicts with an active Site", error.Message);
    }

    private static ApiClient NewClient(string json, HttpStatusCode status = HttpStatusCode.ServiceUnavailable)
    {
        var handler = new FixedResponseHandler(json, status);
        return new ApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") });
    }

    private sealed class FixedResponseHandler(string json, HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/problem+json")
            });
    }
}
