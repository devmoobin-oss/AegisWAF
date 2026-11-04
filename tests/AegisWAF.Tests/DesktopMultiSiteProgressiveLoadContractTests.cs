using System.Text;

namespace AegisWAF.Tests;

public sealed class DesktopMultiSiteProgressiveLoadContractTests
{
    [Fact]
    public void Desktop_renders_site_rows_before_waiting_for_health_summary()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(
            Path.Combine(root, "src", "AegisWAF.Desktop", "Views", "SitesView.xaml.cs"),
            Encoding.UTF8);

        var siteRequest = source.IndexOf("var sites = await _api.GetSitesAsync(ct);", StringComparison.Ordinal);
        var initialRows = source.IndexOf("Grid.ItemsSource = initialRows;", siteRequest, StringComparison.Ordinal);
        var summaryRequest = source.IndexOf("var freshSummary = await TryGetSummaryAsync(ct);", siteRequest, StringComparison.Ordinal);

        Assert.True(siteRequest >= 0, "The Desktop Site view should load the base Site list.");
        Assert.True(initialRows > siteRequest, "The Desktop Site view should render the base rows after the list response.");
        Assert.True(summaryRequest > initialRows, "Health summary loading should happen after the initial rows are visible.");
        Assert.Contains("รายการ Site โหลดแล้ว · กำลังโหลดภาพรวม health trend...", source, StringComparison.Ordinal);
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
