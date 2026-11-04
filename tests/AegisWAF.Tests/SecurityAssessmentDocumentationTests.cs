namespace AegisWAF.Tests;

public sealed class SecurityAssessmentDocumentationTests
{
    [Fact]
    public void Security_assessment_records_the_latest_hosted_regression_evidence_and_boundaries()
    {
        var root = FindRepoRoot();
        var document = File.ReadAllText(Path.Combine(root, "docs", "SECURITY_ASSESSMENT.md"));

        Assert.Contains("Latest Hosted Security Verification evidence", document, StringComparison.Ordinal);
        Assert.Contains("40445541235", document, StringComparison.Ordinal);
        Assert.Contains("TRX_COUNTERS total=509 executed=509 passed=509 failed=0 skipped=0", document, StringComparison.Ordinal);
        Assert.Contains("Code scanning/Advanced Security", document, StringComparison.Ordinal);
        Assert.Contains("SARIF", document, StringComparison.Ordinal);
        Assert.Contains("not a CodeQL pass", document, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "AegisWAF.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
