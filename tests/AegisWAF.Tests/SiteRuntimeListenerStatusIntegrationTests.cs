using System.Text.Json;

namespace AegisWAF.Tests;

[Collection("gateway")]
public sealed class SiteRuntimeListenerStatusIntegrationTests(GatewayFixture fx)
{
    [Fact]
    public async Task Overview_reports_postgres_listener_state_without_raw_connection_details()
    {
        var api = await fx.LoginAdmin();
        var ready = false;
        var readyStatus = 0;
        var readyBody = string.Empty;
        var readyDeadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < readyDeadline)
        {
            using var response = await api.Get("/ready");
            readyStatus = (int)response.StatusCode;
            readyBody = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode)
            {
                ready = true;
                break;
            }

            await Task.Delay(50);
        }

        Assert.True(ready, $"Gateway did not become ready within 15 seconds; last /ready status was {readyStatus}; body={readyBody}");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        JsonElement listener = default;

        while (!timeout.IsCancellationRequested)
        {
            var response = await api.Get("/api/overview");
            response.EnsureSuccessStatusCode();
            var document = await api.Json(response);
            Assert.True(document.TryGetProperty("siteRuntimeListener", out listener));
            Assert.True(document.TryGetProperty("securityEventRetention", out var retention));
            Assert.InRange(retention.GetProperty("retentionHours").GetInt32(), 1, 8_760);
            if (listener.GetProperty("state").GetString() == "connected") break;
            await Task.Delay(50, timeout.Token);
        }

        Assert.Equal("connected", listener.GetProperty("state").GetString());
        Assert.True(listener.GetProperty("isConnected").GetBoolean());
        Assert.True(listener.GetProperty("connectionAttempts").GetInt32() >= 1);
        Assert.True(listener.GetProperty("lastConnectedAt").GetDateTimeOffset() <= DateTimeOffset.UtcNow);
        Assert.DoesNotContain("Password=", listener.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Host=", listener.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }
}
