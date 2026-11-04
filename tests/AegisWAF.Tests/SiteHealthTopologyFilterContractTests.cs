using System.Text;

namespace AegisWAF.Tests;

public sealed class SiteHealthTopologyFilterContractTests
{
    [Fact]
    public void Direct_trend_reads_retention_before_filtering_removed_upstreams()
    {
        var endpoint = Endpoint("app.MapGet(\"/api/sites/{id}/health/trend\"", "app.MapPost(\"/api/sites/{id}/health/alerts/acknowledge\"");

        Assert.Contains("SiteHealthTrendCalculator.MaxWindow", endpoint, StringComparison.Ordinal);
        Assert.Contains("FilterToCurrentUpstreams", endpoint, StringComparison.Ordinal);
        Assert.Contains("site.Upstreams.Select(x => x.Id)", endpoint, StringComparison.Ordinal);
    }

    [Fact]
    public void Acknowledge_and_summary_filter_history_to_the_current_upstream_topology()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AegisWAF.Gateway", "GatewayApp.cs"), Encoding.UTF8);
        var acknowledge = Endpoint(source, "app.MapPost(\"/api/sites/{id}/health/alerts/acknowledge\"", "app.MapGet(\"/api/sites/health/summary\"");
        var summary = Endpoint(source, "app.MapGet(\"/api/sites/health/summary\"", "app.MapPost(\"/api/sites\"");

        Assert.Contains("FilterToCurrentUpstreams", acknowledge, StringComparison.Ordinal);
        Assert.Contains("FilterToCurrentUpstreams", summary, StringComparison.Ordinal);
        Assert.Contains("ListHealthHistoryBySiteAsync(activeSiteIds, SiteHealthTrendCalculator.MaxWindow", summary, StringComparison.Ordinal);
    }

    private static string Endpoint(string startMarker, string endMarker)
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AegisWAF.Gateway", "GatewayApp.cs"), Encoding.UTF8);
        return Endpoint(source, startMarker, endMarker);
    }

    private static string Endpoint(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Could not isolate endpoint starting with {startMarker}.");
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
