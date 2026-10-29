using System.Text;

namespace AegisWAF.Tests;

public sealed class SiteHealthSummaryPagedReadContractTests
{
    [Fact]
    public void Summary_endpoint_uses_a_bounded_database_page_instead_of_loading_all_sites()
    {
        var root = FindRepoRoot();
        var gateway = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "GatewayApp.cs"), Encoding.UTF8);
        var store = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "Sites", "SiteDatabaseStore.cs"), Encoding.UTF8);
        var start = gateway.IndexOf("app.MapGet(\"/api/sites/health/summary\"", StringComparison.Ordinal);
        var end = gateway.IndexOf("app.MapPost(\"/api/sites", start, StringComparison.Ordinal);

        Assert.True(start >= 0 && end > start, "Could not isolate the health summary endpoint.");
        var endpoint = gateway[start..end];
        Assert.Contains("store.ListSummaryPageAsync(boundedLimit, summaryCt)", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("store.ListAsync(summaryCt)", endpoint, StringComparison.Ordinal);
        Assert.Contains("COUNT(*) OVER()", store, StringComparison.Ordinal);
        Assert.Contains("LIMIT @limit", store, StringComparison.Ordinal);
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
