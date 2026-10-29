using System.Text;

namespace AegisWAF.Tests;

public sealed class SiteHealthAlertAcknowledgementFallbackContractTests
{
    [Fact]
    public void Acknowledge_endpoint_uses_recent_process_memory_observations_before_deciding()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AegisWAF.Gateway", "GatewayApp.cs"), Encoding.UTF8);
        var start = source.IndexOf("app.MapPost(\"/api/sites/{id}/health/alerts/acknowledge\"", StringComparison.Ordinal);
        var end = source.IndexOf("app.MapGet(\"/api/sites/health/summary\"", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Could not isolate the health alert acknowledgement endpoint.");
        var endpoint = source[start..end];

        Assert.Contains("SiteHealthHistory history", endpoint, StringComparison.Ordinal);
        Assert.Contains("history.Read(site.Id, SiteHealthTrendCalculator.MaxWindow)", endpoint, StringComparison.Ordinal);
        Assert.Contains("history.HasPending(site.Id)", endpoint, StringComparison.Ordinal);
        Assert.Contains("SiteHealthHistory.Merge(persisted, processMemory, SiteHealthTrendCalculator.MaxWindow)", endpoint, StringComparison.Ordinal);
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
