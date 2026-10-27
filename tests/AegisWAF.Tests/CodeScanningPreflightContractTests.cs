using System.Text;

namespace AegisWAF.Tests;

public sealed class CodeScanningPreflightContractTests
{
    [Fact]
    public void CodeQL_workflow_has_a_fail_closed_code_scanning_preflight()
    {
        var root = FindRepoRoot();
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "security-verification.yml"), Encoding.UTF8);
        var script = File.ReadAllText(Path.Combine(root, "tools", "ci", "check-code-scanning.py"), Encoding.UTF8);

        Assert.Contains("check-code-scanning.py", workflow, StringComparison.Ordinal);
        Assert.Contains("code-scanning/analyses", script, StringComparison.Ordinal);
        Assert.Contains("Code scanning is not enabled", script, StringComparison.Ordinal);
        Assert.Contains("return 1", script, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "AegisWAF.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
