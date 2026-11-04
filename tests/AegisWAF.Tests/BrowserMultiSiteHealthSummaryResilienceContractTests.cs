using System.Text;

namespace AegisWAF.Tests;

public sealed class BrowserMultiSiteHealthSummaryResilienceContractTests
{
    [Fact]
    public void Browser_multi_site_surfaces_unavailable_health_summary_without_hiding_the_boundary()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "wwwroot", "assets", "app.js"), Encoding.UTF8);
        var start = source.IndexOf("async function loadSitesCrud", StringComparison.Ordinal);
        var end = source.IndexOf("const loadSectionWithSiteCrud", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Could not isolate the browser Multi-Site workflow.");
        var sites = source[start..end];

        Assert.Contains("summaryLoadState='loading'", sites, StringComparison.Ordinal);
        Assert.Contains("summaryLoadState='ready'", sites, StringComparison.Ordinal);
        Assert.Contains("summaryLoadState='unavailable'", sites, StringComparison.Ordinal);
        Assert.Contains("id=\"siteHealthSummaryStatus\"", sites, StringComparison.Ordinal);
        Assert.Contains("สถานะในตารางอิงการตรวจล่าสุดเท่านั้น", sites, StringComparison.Ordinal);
        Assert.Contains("summaryStatus.hidden=summaryLoadState==='ready'", sites, StringComparison.Ordinal);
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
