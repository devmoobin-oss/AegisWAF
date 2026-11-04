using System.Text;

namespace AegisWAF.Tests;

public sealed class SiteHealthSummaryPendingScopeContractTests
{
    [Fact]
    public void Browser_clarifies_that_pending_count_is_page_scoped_when_summary_is_truncated()
    {
        var browser = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AegisWAF.Gateway", "wwwroot", "assets", "app.js"), Encoding.UTF8);

        Assert.Contains("countsScope==='returned-sites'", browser, StringComparison.Ordinal);
        Assert.Contains("รายการนี้รอเปิดใช้", browser, StringComparison.Ordinal);
    }

    [Fact]
    public void Desktop_clarifies_that_pending_count_is_page_scoped_when_summary_is_truncated()
    {
        var desktop = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AegisWAF.Desktop", "Views", "SitesView.xaml.cs"), Encoding.UTF8);

        Assert.Contains("summary.CountsScope == \"returned-sites\"", desktop, StringComparison.Ordinal);
        Assert.Contains("รายการนี้รอเปิดใช้", desktop, StringComparison.Ordinal);
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
