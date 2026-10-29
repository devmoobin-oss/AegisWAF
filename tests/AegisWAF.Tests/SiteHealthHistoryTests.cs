using AegisWAF.Gateway.Sites;

namespace AegisWAF.Tests;

public sealed class SiteHealthHistoryTests
{
    [Fact]
    public void History_is_bounded_per_site_and_returns_newest_observations_first()
    {
        var history = new SiteHealthHistory(maxObservationsPerSite: 2);
        var now = DateTimeOffset.UtcNow;
        history.Record("shop", [
            new SiteHealthProbeResult("one", "http://user:password@origin-a.test/", "healthy", null, now),
            new SiteHealthProbeResult("one", "http://user:password@origin-a.test/", "unhealthy", "timeout", now.AddSeconds(1)),
            new SiteHealthProbeResult("one", "http://user:password@origin-a.test/", "healthy", null, now.AddSeconds(2))
        ]);

        var observations = history.Read("shop", 100);

        Assert.Equal(2, observations.Count);
        Assert.Equal("healthy", observations[0].Health);
        Assert.Equal("unhealthy", observations[1].Health);
        Assert.DoesNotContain("password", string.Join('|', observations.Select(x => x.Reason)), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void History_limit_is_bounded_and_unknown_sites_are_empty()
    {
        var history = new SiteHealthHistory(maxObservationsPerSite: 4);
        var now = DateTimeOffset.UtcNow;
        history.Record("shop", Enumerable.Range(0, 4).Select(i => new SiteHealthProbeResult("u" + i, "origin", "healthy", null, now.AddSeconds(i))).ToArray());

        Assert.Equal(2, history.Read("shop", 2).Count);
        Assert.Empty(history.Read("missing", 100));
    }

    [Fact]
    public void History_evicts_the_least_recently_used_site_when_global_bound_is_reached()
    {
        var history = new SiteHealthHistory(maxObservationsPerSite: 2, maxSites: 2);
        var now = DateTimeOffset.UtcNow;
        var observation = new SiteHealthProbeResult("one", "origin", "healthy", null, now);

        history.Record("old", [observation]);
        history.Record("recent", [observation with { CheckedAt = now.AddSeconds(1) }]);
        _ = history.Read("old", 1); // Make old the most recently used entry.
        history.Record("new", [observation with { CheckedAt = now.AddSeconds(2) }]);

        Assert.NotEmpty(history.Read("old", 1));
        Assert.NotEmpty(history.Read("new", 1));
        Assert.Empty(history.Read("recent", 1));
    }
}
