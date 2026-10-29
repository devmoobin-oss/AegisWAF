using AegisWAF.Gateway.Sites;

namespace AegisWAF.Tests;

public sealed class SiteRuntimeChangeNotificationTests
{
    [Fact]
    public void Payload_is_bounded_redacted_and_round_trips_only_the_wakeup_contract()
    {
        var payload = SiteRuntimeChangeNotification.CreatePayload("site.update", "affiliateiq");

        Assert.True(payload.Length <= SiteRuntimeChangeNotification.MaxPayloadLength);
        Assert.DoesNotContain("http", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("localhost", payload, StringComparison.OrdinalIgnoreCase);
        Assert.True(SiteRuntimeChangeNotification.TryParse(payload, out var change));
        Assert.Equal("site.update", change.Operation);
        Assert.Equal("affiliateiq", change.SiteId);
        Assert.Equal(SiteRuntimeChangeNotification.Schema, change.Schema);
    }

    [Fact]
    public void Payload_rejects_unknown_or_oversized_values()
    {
        Assert.Throws<ArgumentException>(() => SiteRuntimeChangeNotification.CreatePayload("site.drop-table", "site-a"));
        Assert.False(SiteRuntimeChangeNotification.TryParse("{\"schema\":\"wrong\",\"operation\":\"site.update\",\"siteId\":\"site-a\"}", out _));
        Assert.False(SiteRuntimeChangeNotification.TryParse(new string('x', SiteRuntimeChangeNotification.MaxPayloadLength + 1), out _));
    }

    [Fact]
    public void Payload_with_wrong_json_types_is_rejected_without_throwing()
    {
        var malformedPayloads = new[]
        {
            "{\"schema\":[],\"operation\":\"site.update\",\"siteId\":\"site-a\"}",
            "{\"schema\":\"aegiswaf.site.v1\",\"operation\":42,\"siteId\":\"site-a\"}",
            "{\"schema\":\"aegiswaf.site.v1\",\"operation\":\"site.update\",\"siteId\":123}"
        };

        foreach (var payload in malformedPayloads)
            Assert.False(SiteRuntimeChangeNotification.TryParse(payload, out _));
    }
}
