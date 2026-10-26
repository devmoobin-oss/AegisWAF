namespace AegisWAF.Tests;

public sealed class HostedRegressionEvidenceContractTests
{
    [Fact]
    public void Security_workflow_publishes_and_verifies_full_regression_results()
    {
        var root = FindRepoRoot();
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "security-verification.yml"));
        var verifier = File.ReadAllText(Path.Combine(root, "tools", "ci", "verify-trx-results.py"));

        Assert.Contains("full-regression.trx", workflow, StringComparison.Ordinal);
        Assert.Contains("verify-trx-results.py", workflow, StringComparison.Ordinal);
        Assert.Contains("TestResults", workflow, StringComparison.Ordinal);
        Assert.Contains("if: always()", workflow, StringComparison.Ordinal);
        Assert.Contains("notExecuted", verifier, StringComparison.Ordinal);
        Assert.Contains("failed", verifier, StringComparison.Ordinal);
        Assert.Contains("total", verifier, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "AegisWAF.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
