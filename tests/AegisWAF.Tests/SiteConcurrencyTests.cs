using AegisWAF.Gateway.Sites;

namespace AegisWAF.Tests;

public sealed class SiteConcurrencyTests
{
    [Fact]
    public void Strong_etag_round_trips_a_positive_version()
    {
        var etag = SiteConcurrency.ToEtag(42);

        Assert.Equal("\"42\"", etag);
        Assert.True(SiteConcurrency.TryParseEtag(etag, out var version));
        Assert.Equal(42, version);
    }

    [Fact]
    public void Weak_and_malformed_etags_are_rejected()
    {
        Assert.False(SiteConcurrency.TryParseEtag("W/\"42\"", out _));
        Assert.False(SiteConcurrency.TryParseEtag("42", out _));
        Assert.False(SiteConcurrency.TryParseEtag("\"0\"", out _));
    }
}
