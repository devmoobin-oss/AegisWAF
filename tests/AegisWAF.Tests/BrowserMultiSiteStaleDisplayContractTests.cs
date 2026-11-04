using System.Text;

namespace AegisWAF.Tests;

public sealed class BrowserMultiSiteStaleDisplayContractTests
{
    [Fact]
    public void Browser_multi_site_does_not_present_stale_history_as_a_current_crisis()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "wwwroot", "assets", "app.js"), Encoding.UTF8);
        var start = source.IndexOf("async function loadSitesCrud", StringComparison.Ordinal);
        var end = source.IndexOf("const loadSectionWithSiteCrud", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Could not isolate the browser Multi-Site workflow.");
        var sites = source[start..end];

        Assert.Contains("summary?.isStale", sites, StringComparison.Ordinal);
        Assert.Contains("summary?.effectiveAlertState||summary?.alertState", sites, StringComparison.Ordinal);
        Assert.Contains("'stale'", sites, StringComparison.Ordinal);
        Assert.Contains("ข้อมูลสรุปเก่า", sites, StringComparison.Ordinal);
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
