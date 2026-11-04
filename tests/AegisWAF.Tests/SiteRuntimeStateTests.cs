using AegisWAF.Gateway.Sites;

namespace AegisWAF.Tests;

public sealed class SiteRuntimeStateTests
{
    [Fact]
    public void Equivalent_active_snapshot_is_not_pending()
    {
        var site = Site();

        Assert.False(SiteRuntimeState.IsPendingRestart(site, site with { }));
    }

    [Fact]
    public void Missing_active_snapshot_is_pending()
    {
        Assert.True(SiteRuntimeState.IsPendingRestart(Site(), null));
    }

    [Fact]
    public void Version_or_configuration_drift_is_pending()
    {
        var site = Site();

        Assert.True(SiteRuntimeState.IsPendingRestart(site, site with { Version = site.Version + 1 }));
        Assert.True(SiteRuntimeState.IsPendingRestart(site, site with { Hostnames = ["new.example.com"] }));
    }

    private static GatewaySite Site() => new(
        "site-a",
        true,
        ["app.example.com"],
        ["/"],
        [new SiteUpstream("primary", new Uri("http://origin.example.com/"))],
        "default",
        new SiteRateLimitPolicy(),
        new SiteEgressPolicy(["origin.example.com"], [80], ["http"]));
}
