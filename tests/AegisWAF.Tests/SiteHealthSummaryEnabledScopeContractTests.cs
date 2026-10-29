using System.Text;

namespace AegisWAF.Tests;

public sealed class SiteHealthSummaryEnabledScopeContractTests
{
    [Fact]
    public void Summary_endpoint_uses_an_enabled_only_summary_page()
    {
        var root = FindRepoRoot();
        var gateway = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "GatewayApp.cs"), Encoding.UTF8);
        var store = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "Sites", "SiteDatabaseStore.cs"), Encoding.UTF8);

        var start = gateway.IndexOf("app.MapGet(\"/api/sites/health/summary\"", StringComparison.Ordinal);
        var end = gateway.IndexOf("app.MapPost(\"/api/sites", start, StringComparison.Ordinal);

        Assert.True(start >= 0 && end > start, "Could not isolate the health summary endpoint.");
        Assert.Contains("store.ListEnabledSummaryPageAsync(boundedLimit, summaryCt)", gateway[start..end], StringComparison.Ordinal);
        Assert.Contains("WHERE enabled", store, StringComparison.OrdinalIgnoreCase);
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
