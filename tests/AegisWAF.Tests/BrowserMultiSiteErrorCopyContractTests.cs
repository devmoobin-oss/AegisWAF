using System.Text;

namespace AegisWAF.Tests;

public sealed class BrowserMultiSiteErrorCopyContractTests
{
    [Fact]
    public void Multi_site_problem_codes_have_bounded_thai_recovery_copy()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "wwwroot", "assets", "app.js"), Encoding.UTF8);
        var start = source.IndexOf("const apiErrorCopy", StringComparison.Ordinal);
        var end = source.IndexOf("let appDialogSequence", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Could not isolate the browser API error mapper.");
        var mapper = source[start..end];

        Assert.Contains("detail.code", mapper, StringComparison.Ordinal);
        Assert.Contains("site_mutation_busy", mapper, StringComparison.Ordinal);
        Assert.Contains("site_mutation_read_timeout", mapper, StringComparison.Ordinal);
        Assert.Contains("site_mutation_write_timeout", mapper, StringComparison.Ordinal);
        Assert.Contains("site_runtime_activation_timeout", mapper, StringComparison.Ordinal);
        Assert.Contains("site_health_write_timeout", mapper, StringComparison.Ordinal);
        Assert.Contains("site_health_summary_timeout", mapper, StringComparison.Ordinal);
        Assert.Contains("site_runtime_activation_failed", mapper, StringComparison.Ordinal);
        Assert.Contains("slice(0,512)", mapper, StringComparison.Ordinal);
        Assert.DoesNotContain("traceId", mapper, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AegisWAF.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
