using System.Text;

namespace AegisWAF.Tests;

public sealed class SiteHealthSummaryProcessMemoryFallbackContractTests
{
    [Fact]
    public void Summary_endpoint_uses_process_memory_when_only_health_history_read_fails()
    {
        var endpoint = Endpoint("app.MapGet(\"/api/sites/health/summary\"", "app.MapPost(\"/api/sites\"");

        Assert.Contains("SiteHealthHistory history", endpoint, StringComparison.Ordinal);
        Assert.Contains("history.Read(siteId", endpoint, StringComparison.Ordinal);
        Assert.Contains("SiteHealthHistory.Merge", endpoint, StringComparison.Ordinal);
        Assert.Contains("historyPersisted = false", endpoint, StringComparison.Ordinal);
        Assert.Contains("historyRetention", endpoint, StringComparison.Ordinal);
        Assert.Contains("postgresql+process-memory", endpoint, StringComparison.Ordinal);
    }

    [Fact]
    public void Summary_keeps_persistence_integrity_failures_as_unavailable()
    {
        var endpoint = Endpoint("app.MapGet(\"/api/sites/health/summary\"", "app.MapPost(\"/api/sites\"");

        Assert.Contains("catch (InvalidDataException)", endpoint, StringComparison.Ordinal);
        Assert.Contains("site_persistence_invalid", endpoint, StringComparison.Ordinal);
    }

    private static string Endpoint(string startMarker, string endMarker)
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "GatewayApp.cs"), Encoding.UTF8);
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
