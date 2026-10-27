using System.Text;

namespace AegisWAF.Tests;

public sealed class BrowserMultiSiteMutationOutcomeContractTests
{
    [Fact]
    public void Browser_multi_site_distinguishes_committed_mutation_from_refresh_failure()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "wwwroot", "assets", "app.js"), Encoding.UTF8);
        var start = source.IndexOf("async function loadSitesCrud", StringComparison.Ordinal);
        var end = source.IndexOf("const loadSectionWithSiteCrud", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Could not isolate the browser Multi-Site workflow.");
        var sites = source[start..end];

        Assert.Contains("let mutationCommitted=false", sites, StringComparison.Ordinal);
        Assert.Contains("mutationCommitted=true", sites, StringComparison.Ordinal);
        Assert.Contains("บันทึก Site สำเร็จแล้ว แต่โหลดรายการใหม่ไม่สำเร็จ", sites, StringComparison.Ordinal);
        Assert.Contains("mutationCommitted?", sites, StringComparison.Ordinal);
        Assert.Contains("'บันทึก Site ไม่สำเร็จ: '", sites, StringComparison.Ordinal);
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
