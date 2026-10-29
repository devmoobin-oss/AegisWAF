using System.Text;
using AegisWAF.Gateway.Sites;

namespace AegisWAF.Tests;

public sealed class SiteHealthSummaryCountScopeContractTests
{
    [Theory]
    [InlineData(0, 0, "all-enabled-sites")]
    [InlineData(1, 1, "all-enabled-sites")]
    [InlineData(101, 100, "returned-sites")]
    [InlineData(100, 99, "returned-sites")]
    public void Count_scope_matches_the_returned_summary_boundary(long totalSites, int returnedSites, string expected)
    {
        Assert.Equal(expected, SiteHealthSummaryCountScope.For(totalSites, returnedSites));
    }

    [Fact]
    public void Operator_surfaces_expose_count_scope_when_summary_is_truncated()
    {
        var root = FindRepoRoot();
        var gateway = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "GatewayApp.cs"), Encoding.UTF8);
        var models = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Client", "Models.cs"), Encoding.UTF8);
        var desktop = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Desktop", "Views", "SitesView.xaml.cs"), Encoding.UTF8);
        var browser = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "wwwroot", "assets", "app.js"), Encoding.UTF8);

        Assert.Contains("countsScope = SiteHealthSummaryCountScope.For(totalSites, projected.Length)", gateway, StringComparison.Ordinal);
        Assert.Contains("string CountsScope = \"all-enabled-sites\"", models, StringComparison.Ordinal);
        Assert.Contains("summary.CountsScope", desktop, StringComparison.Ordinal);
        Assert.Contains("countsScope", browser, StringComparison.Ordinal);
        Assert.Contains("ตัวเลขสรุปนับจาก Site ที่แสดงเท่านั้น", browser, StringComparison.Ordinal);
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
