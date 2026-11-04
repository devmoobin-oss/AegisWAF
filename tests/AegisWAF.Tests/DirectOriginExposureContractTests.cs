namespace AegisWAF.Tests;

public sealed class DirectOriginExposureContractTests
{
    [Fact]
    public void Loopback_origin_verification_contract_is_present()
    {
        var root = FindRepoRoot();
        var verifier = File.ReadAllText(Path.Combine(root, "tools", "security", "Assert-LoopbackOrigin.ps1"));
        var selfTest = File.ReadAllText(Path.Combine(root, "tools", "security", "test-loopback-origin.ps1"));
        var readme = File.ReadAllText(Path.Combine(root, "tools", "security", "README.md"));
        var guide = File.ReadAllText(Path.Combine(root, "docs", "NEXTJS_ORIGIN_HARDENING.md"));
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "security-verification.yml"));

        Assert.Contains("Get-NetTCPConnection", verifier, StringComparison.Ordinal);
        Assert.Contains("0.0.0.0", verifier, StringComparison.Ordinal);
        Assert.Contains("No LISTEN listener", verifier, StringComparison.Ordinal);
        Assert.Contains("LOOPBACK_ORIGIN_TEST_PASS", selfTest, StringComparison.Ordinal);
        Assert.Contains("next start -H 127.0.0.1 -p 4000", readme, StringComparison.Ordinal);
        Assert.Contains("affiliateiq.localhost", guide, StringComparison.Ordinal);
        Assert.Contains("http://127.0.0.1:4000/", guide, StringComparison.Ordinal);
        Assert.Contains("direct-origin-exposure:", workflow, StringComparison.Ordinal);
        Assert.Contains("test-loopback-origin.ps1", workflow, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "AegisWAF.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
