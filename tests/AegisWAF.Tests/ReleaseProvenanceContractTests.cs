namespace AegisWAF.Tests;

public sealed class ReleaseProvenanceContractTests
{
    [Fact]
    public void Release_manifest_tool_and_opt_in_workflow_contract_are_present()
    {
        var root = FindRepoRoot();
        var writer = File.ReadAllText(Path.Combine(root, "tools", "release", "Write-ReleaseManifest.ps1"));
        var selfTest = File.ReadAllText(Path.Combine(root, "tools", "release", "test-release-manifest.ps1"));
        var verifier = File.ReadAllText(Path.Combine(root, "tools", "release", "Verify-ReleaseManifest.ps1"));
        var verifierTest = File.ReadAllText(Path.Combine(root, "tools", "release", "test-release-verifier.ps1"));
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "security-verification.yml"));

        Assert.Contains("sha256", writer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("workingTreeClean", writer, StringComparison.Ordinal);
        Assert.Contains("signed = $false", writer, StringComparison.Ordinal);
        Assert.Contains("ConvertTo-Json", writer, StringComparison.Ordinal);
        Assert.Contains("tamper_hash_changed=true", selfTest, StringComparison.Ordinal);
        Assert.Contains("Manifest SHA-256 mismatch", verifier, StringComparison.Ordinal);
        Assert.Contains("Unmanifested bundle files", verifier, StringComparison.Ordinal);
        Assert.Contains("tamper_rejected=true", verifierTest, StringComparison.Ordinal);
        Assert.Contains("publish_desktop:", workflow, StringComparison.Ordinal);
        Assert.Contains("release-provenance:", workflow, StringComparison.Ordinal);
        Assert.Contains("Write-ReleaseManifest.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("actions/upload-artifact@v4", workflow, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "AegisWAF.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
