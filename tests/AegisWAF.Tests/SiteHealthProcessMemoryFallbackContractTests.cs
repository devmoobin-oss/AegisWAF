using System.Text;

namespace AegisWAF.Tests;

public sealed class SiteHealthProcessMemoryFallbackContractTests
{
    [Fact]
    public void History_endpoint_merges_process_memory_and_reports_the_persistence_boundary()
    {
        var endpoint = Endpoint("app.MapGet(\"/api/sites/{id}/health/history\"", "app.MapGet(\"/api/sites/{id}/health/trend\"");

        Assert.Contains("SiteHealthHistory history", endpoint, StringComparison.Ordinal);
        Assert.Contains("history.Read(site.Id", endpoint, StringComparison.Ordinal);
        Assert.Contains("SiteHealthHistory.Merge(persisted, processMemory", endpoint, StringComparison.Ordinal);
        Assert.Contains("databaseHistoryAvailable = false", endpoint, StringComparison.Ordinal);
        Assert.Contains("var historyPersisted = databaseHistoryAvailable && !hasPendingHistory", endpoint, StringComparison.Ordinal);
        Assert.Contains("historyRetention", endpoint, StringComparison.Ordinal);
    }

    [Fact]
    public void Trend_endpoint_uses_the_same_process_memory_fallback_before_alert_calculation()
    {
        var endpoint = Endpoint("app.MapGet(\"/api/sites/{id}/health/trend\"", "app.MapPost(\"/api/sites/{id}/health/alerts/acknowledge\"");

        Assert.Contains("SiteHealthHistory history", endpoint, StringComparison.Ordinal);
        Assert.Contains("history.Read(site.Id", endpoint, StringComparison.Ordinal);
        Assert.Contains("SiteHealthHistory.Merge(persisted, processMemory", endpoint, StringComparison.Ordinal);
        Assert.Contains("historyRetention", endpoint, StringComparison.Ordinal);
        Assert.Contains("FilterToCurrentUpstreams", endpoint, StringComparison.Ordinal);
    }

    [Fact]
    public void Manual_probe_marks_only_successfully_persisted_observations_as_durable()
    {
        var endpoint = Endpoint("app.MapPost(\"/api/sites/{id}/health\"", "app.MapGet(\"/api/sites/{id}/health/history\"");

        Assert.Contains("history.MarkPersisted(site.Id, persistedObservations)", endpoint, StringComparison.Ordinal);
        Assert.Contains("var hasPendingHistory = history.HasPending(site.Id)", endpoint, StringComparison.Ordinal);
        Assert.Contains("postgresql+process-memory", endpoint, StringComparison.Ordinal);
    }

    [Fact]
    public void History_and_trend_can_resolve_the_active_site_when_the_database_read_is_temporarily_unavailable()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AegisWAF.Gateway", "GatewayApp.cs"), Encoding.UTF8);
        var history = Endpoint(source, "app.MapGet(\"/api/sites/{id}/health/history\"", "app.MapGet(\"/api/sites/{id}/health/trend\"");
        var trend = Endpoint(source, "app.MapGet(\"/api/sites/{id}/health/trend\"", "app.MapPost(\"/api/sites/{id}/health/alerts/acknowledge\"");

        Assert.Contains("SiteRegistry registry", history, StringComparison.Ordinal);
        Assert.Contains("registry.TryGetById(normalizedId, out site)", history, StringComparison.Ordinal);
        Assert.Contains("SiteRegistry registry", trend, StringComparison.Ordinal);
        Assert.Contains("registry.TryGetById(normalizedId, out site)", trend, StringComparison.Ordinal);
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
