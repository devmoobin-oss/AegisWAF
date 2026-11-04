using System.Text;

namespace AegisWAF.Tests;

public sealed class BrowserMultiSiteHealthSummaryRetentionContractTests
{
    [Fact]
    public void Browser_multi_site_exposes_a_non_critical_warning_when_summary_history_is_not_fully_durable()
    {
        var sites = SourceSlice("async function loadSitesCrud", "const loadSectionWithSiteCrud");

        Assert.Contains("historyPersisted", sites, StringComparison.Ordinal);
        Assert.Contains("historyRetention", sites, StringComparison.Ordinal);
        Assert.Contains("summaryLoadState==='ready'&&summaryMeta.historyPersisted===false", sites, StringComparison.Ordinal);
        Assert.Contains("ข้อมูลหน่วยความจำชั่วคราว", sites, StringComparison.Ordinal);
        Assert.Contains("summaryPending", sites, StringComparison.Ordinal);
        Assert.Contains("summaryDegraded||summaryTruncated||summaryPending?'warning'", sites, StringComparison.Ordinal);
    }

    private static string SourceSlice(string startMarker, string endMarker)
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AegisWAF.Gateway", "wwwroot", "assets", "app.js"), Encoding.UTF8);
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Could not isolate the browser Multi-Site workflow.");
        return source[start..end];
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
