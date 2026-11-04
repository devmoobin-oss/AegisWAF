using AegisWAF.Gateway.Sites;

namespace AegisWAF.Tests;

public sealed class SiteProxyHealthTests
{
    [Theory]
    [InlineData("Healthy", "Unknown", "healthy")]
    [InlineData("Unknown", "Unknown", "unknown")]
    [InlineData("Unhealthy", "Unknown", "unhealthy")]
    [InlineData("Healthy", "Unhealthy", "unhealthy")]
    public void Projects_yarp_active_and_passive_health_without_treating_unknown_as_healthy(string active, string passive, string expected)
    {
        Assert.Equal(expected, SiteProxyHealth.Status(active, passive));
    }

    [Fact]
    public void Unknown_destination_remains_available_until_yarp_marks_it_unhealthy()
    {
        Assert.True(SiteProxyHealth.IsAvailable("Unknown", "Unknown"));
        Assert.False(SiteProxyHealth.IsAvailable("Unhealthy", "Unhealthy"));
    }

    [Fact]
    public void Readiness_can_require_a_known_healthy_destination()
    {
        Assert.False(SiteProxyHealth.IsAvailable("Unknown", "Unknown", requireKnownHealthy: true));
        Assert.True(SiteProxyHealth.IsAvailable("Healthy", "Unknown", requireKnownHealthy: true));
        Assert.False(SiteProxyHealth.IsAvailable("Unhealthy", "Healthy", requireKnownHealthy: true));
    }
}
