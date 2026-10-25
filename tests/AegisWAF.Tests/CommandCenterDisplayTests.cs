using AegisWAF.Client;

namespace AegisWAF.Tests;

public sealed class CommandCenterDisplayTests
{
    [Fact]
    public void Unmatched_and_not_forwarded_rows_are_explicit_instead_of_blank()
    {
        var row = new CommandCenterTrafficDto(DateTimeOffset.UtcNow, "trace", "127.0.0.1", null, "127.0.0.1",
            "GET", "/a/aa/", null, null, "ALLOWED", 421, null, [], 0);

        Assert.Equal("UNMATCHED / 127.0.0.1", row.SiteHostDisplay);
        Assert.Equal("NOT FORWARDED", row.UpstreamDisplay);
    }
}
