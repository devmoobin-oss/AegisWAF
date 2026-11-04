using System.Text;

namespace AegisWAF.Tests;

public sealed class SiteHealthSummaryTruncationContractTests
{
    [Fact]
    public void Summary_endpoint_reports_total_returned_and_truncated_site_counts()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AegisWAF.Gateway", "GatewayApp.cs"), Encoding.UTF8);
        var start = source.IndexOf("app.MapGet(\"/api/sites/health/summary\"", StringComparison.Ordinal);
        var end = source.IndexOf("app.MapPost(\"/api/sites", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Could not isolate the health summary endpoint.");
        var endpoint = source[start..end];

        Assert.Contains("summaryPage.TotalSites", endpoint, StringComparison.Ordinal);
        Assert.Contains("totalSites,", endpoint, StringComparison.Ordinal);
        Assert.Contains("returnedSites = projected.Length", endpoint, StringComparison.Ordinal);
        Assert.Contains("truncated = totalSites > projected.Length", endpoint, StringComparison.Ordinal);
    }

    [Fact]
    public void Client_and_operator_surfaces_expose_summary_truncation()
    {
        var root = FindRepoRoot();
        var models = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Client", "Models.cs"), Encoding.UTF8);
        var desktop = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Desktop", "Views", "SitesView.xaml.cs"), Encoding.UTF8);
        var browser = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "wwwroot", "assets", "app.js"), Encoding.UTF8);

        Assert.Contains("int ReturnedSites = 0", models, StringComparison.Ordinal);
        Assert.Contains("bool Truncated = false", models, StringComparison.Ordinal);
        Assert.Contains("summary.Truncated", desktop, StringComparison.Ordinal);
        Assert.Contains("summary.truncated", browser, StringComparison.Ordinal);
        Assert.Contains("จาก", desktop, StringComparison.Ordinal);
        Assert.Contains("จาก", browser, StringComparison.Ordinal);
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
