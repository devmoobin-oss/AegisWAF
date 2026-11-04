using System.Text;

namespace AegisWAF.Tests;

public sealed class SiteHealthSummaryPendingReadScopeContractTests
{
    [Fact]
    public void Summary_reads_health_history_only_for_runtime_active_sites()
    {
        var endpoint = Endpoint("app.MapGet(\"/api/sites/health/summary\"", "app.MapPost(\"/api/sites\"");

        Assert.Contains("activeSiteIds", endpoint, StringComparison.Ordinal);
        Assert.Contains("activeSiteIds.Length == 0", endpoint, StringComparison.Ordinal);
        Assert.Contains("ListHealthHistoryBySiteAsync(activeSiteIds", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("ListHealthHistoryBySiteAsync(siteIds", endpoint, StringComparison.Ordinal);
        Assert.Contains("activeSiteIds.Any(history.HasPending)", endpoint, StringComparison.Ordinal);
    }

    [Fact]
    public void Summary_reads_alert_acknowledgements_only_for_runtime_active_sites()
    {
        var endpoint = Endpoint("app.MapGet(\"/api/sites/health/summary\"", "app.MapPost(\"/api/sites\"");

        Assert.Contains("ListHealthAlertAcknowledgementsAsync(activeSiteIds", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("ListHealthAlertAcknowledgementsAsync(siteIds", endpoint, StringComparison.Ordinal);
    }

    private static string Endpoint(string startMarker, string endMarker)
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AegisWAF.Gateway", "GatewayApp.cs"), Encoding.UTF8);
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Could not isolate the health summary endpoint.");
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
