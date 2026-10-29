using System.Text;

namespace AegisWAF.Tests;

public sealed class SiteSingleReadEfficiencyContractTests
{
    [Fact]
    public void Site_store_exposes_a_parameterized_single_site_read()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AegisWAF.Gateway", "Sites", "SiteDatabaseStore.cs"), Encoding.UTF8);

        Assert.Contains("Task<GatewaySite?> FindByIdAsync", source, StringComparison.Ordinal);
        Assert.Contains("WHERE site_id=@site_id", source, StringComparison.Ordinal);
        Assert.Contains("cmd.Parameters.AddWithValue(\"site_id\", siteId)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Single_site_health_reads_do_not_load_the_entire_site_table()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AegisWAF.Gateway", "GatewayApp.cs"), Encoding.UTF8);
        var endpoints = new[]
        {
            Endpoint(source, "app.MapPost(\"/api/sites/{id}/health\"", "app.MapGet(\"/api/sites/{id}/health/history\""),
            Endpoint(source, "app.MapGet(\"/api/sites/{id}/health/history\"", "app.MapGet(\"/api/sites/{id}/health/trend\""),
            Endpoint(source, "app.MapGet(\"/api/sites/{id}/health/trend\"", "app.MapPost(\"/api/sites/{id}/health/alerts/acknowledge\""),
            Endpoint(source, "app.MapPost(\"/api/sites/{id}/health/alerts/acknowledge\"", "app.MapGet(\"/api/sites/health/summary\"")
        };

        foreach (var endpoint in endpoints)
        {
            Assert.Contains("store.FindByIdAsync", endpoint, StringComparison.Ordinal);
            Assert.DoesNotContain("store.ListAsync", endpoint, StringComparison.Ordinal);
        }
    }

    private static string Endpoint(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Could not isolate endpoint {startMarker}.");
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
