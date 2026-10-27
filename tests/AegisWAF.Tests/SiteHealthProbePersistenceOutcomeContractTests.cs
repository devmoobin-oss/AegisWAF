using System.Text;

namespace AegisWAF.Tests;

public sealed class SiteHealthProbePersistenceOutcomeContractTests
{
    [Fact]
    public void Manual_health_endpoint_exposes_the_history_persistence_boundary()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "GatewayApp.cs"), Encoding.UTF8);
        var start = source.IndexOf("app.MapPost(\"/api/sites/{id}/health\"", StringComparison.Ordinal);
        var end = source.IndexOf("app.MapGet(\"/api/sites/{id}/health/history\"", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Could not isolate the manual health endpoint.");
        var endpoint = source[start..end];

        Assert.Contains("historyPersisted", endpoint, StringComparison.Ordinal);
        Assert.Contains("historyRetention", endpoint, StringComparison.Ordinal);
    }

    [Fact]
    public void Browser_health_probe_warns_when_the_result_was_not_persisted()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "wwwroot", "assets", "app.js"), Encoding.UTF8);
        var start = source.IndexOf("async function loadSitesCrud", StringComparison.Ordinal);
        var end = source.IndexOf("const loadSectionWithSiteCrud", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Could not isolate the browser Multi-Site workflow.");
        var sites = source[start..end];

        Assert.Contains("result.historyPersisted===false", sites, StringComparison.Ordinal);
        Assert.Contains("ยังบันทึกประวัติ Health ไม่ได้", sites, StringComparison.Ordinal);
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
