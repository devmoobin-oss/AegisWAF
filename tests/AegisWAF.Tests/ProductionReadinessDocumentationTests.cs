namespace AegisWAF.Tests;

public sealed class ProductionReadinessDocumentationTests
{
    [Fact]
    public void Production_documents_distinguish_green_hosted_gates_from_pending_codeql()
    {
        var root = FindRepoRoot();
        var readiness = File.ReadAllText(Path.Combine(root, "docs", "PRODUCTION_READINESS.md"));
        var security = File.ReadAllText(Path.Combine(root, "docs", "SECURITY.md"));
        var progress = File.ReadAllText(Path.Combine(root, "docs", "PROGRESS.md"));

        Assert.Contains("40444181184", readiness, StringComparison.Ordinal);
        Assert.Contains("505/505", readiness, StringComparison.Ordinal);
        Assert.Contains("Code scanning/Advanced Security", readiness, StringComparison.Ordinal);
        Assert.Contains("40444181184", security, StringComparison.Ordinal);
        Assert.Contains("secret scan", security, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SBOM", security, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CodeQL", security, StringComparison.Ordinal);
        Assert.Contains("505 passed, 0 failed, 0 skipped", progress, StringComparison.Ordinal);
        Assert.DoesNotContain("continuous SAST/secret/SBOM scanning remain open gates", security, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("still needs a successful hosted run", readiness, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "AegisWAF.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
