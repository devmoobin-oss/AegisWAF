using AegisWAF.Gateway;

namespace AegisWAF.Tests;

public sealed class SsrfGuardTests
{
    [Theory]
    [InlineData("http://127.0.0.1:8080/admin")]
    [InlineData("http://10.0.0.4/health")]
    [InlineData("http://[::1]/admin")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://metadata.google.internal/computeMetadata/v1")]
    [InlineData("url=http%3A%2F%2F127.0.0.1%2Fadmin")]
    [InlineData("file:///etc/passwd")]
    [InlineData("gopher://127.0.0.1:6379/_PING")]
    [InlineData("http://user:pass@192.168.1.4:8080/")]
    public void Blocks_private_metadata_and_dangerous_targets(string input)
    {
        var decision = SsrfGuard.Check(input);
        Assert.True(decision.Blocked, decision.Reason);
        Assert.Equal("A10", decision.OwaspCode);
    }

    [Theory]
    [InlineData("https://public.example/path")]
    [InlineData("redirect=https://docs.example/help")]
    [InlineData("plain text with no URL")]
    [InlineData("http://203.0.113.8:8443/")]
    public void Allows_public_url_and_ordinary_text(string input)
    {
        var decision = SsrfGuard.Check(input);
        Assert.False(decision.Blocked, decision.Reason);
    }

    [Theory]
    [InlineData("http://[::ffff:127.0.0.1]/")]
    [InlineData("http://0x7f000001/")]
    [InlineData("http://127.1/")]
    public void Handles_ipv4_mapped_and_non_canonical_loopback_forms(string input)
    {
        Assert.True(SsrfGuard.Check(input).Blocked);
    }

    [Theory]
    [InlineData("http://[fd00::1]/admin")]
    [InlineData("http://[fc00::dead]/metadata")]
    [InlineData("http://100.64.0.1/internal")]
    [InlineData("http://[ff02::1]/all")]
    public void Blocks_private_reserved_and_multicast_targets(string input)
    {
        var decision = SsrfGuard.Check(input);
        Assert.True(decision.Blocked, decision.Reason);
    }

    [Fact]
    public async Task Dns_aware_check_keeps_direct_private_targets_blocked()
    {
        var decision = await SsrfGuard.CheckAsync("http://127.0.0.1/admin", resolveDns: true);
        Assert.True(decision.Blocked);
    }
}
