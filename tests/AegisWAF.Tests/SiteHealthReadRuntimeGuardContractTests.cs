using System.Text;

namespace AegisWAF.Tests;

public sealed class SiteHealthReadRuntimeGuardContractTests
{
    [Fact]
    public void History_endpoint_short_circuits_before_merging_pending_restart_history()
    {
        var endpoint = Endpoint("app.MapGet(\"/api/sites/{id}/health/history\"", "app.MapGet(\"/api/sites/{id}/health/trend\"");

        Assert.Contains("registry.TryGetById(site.Id, out var active)", endpoint, StringComparison.Ordinal);
        Assert.Contains("if (SiteRuntimeState.IsPendingRestart(site, active))", endpoint, StringComparison.Ordinal);
        Assert.Contains("pendingRestart = true", endpoint, StringComparison.Ordinal);
        Assert.Contains("activationStatus = \"pending-restart\"", endpoint, StringComparison.Ordinal);
        Assert.Contains("historyRetention = \"none\"", endpoint, StringComparison.Ordinal);
        Assert.Contains("observations = Array.Empty<object>()", endpoint, StringComparison.Ordinal);
        Assert.True(endpoint.IndexOf("SiteRuntimeState.IsPendingRestart", StringComparison.Ordinal) < endpoint.IndexOf("var processMemory", StringComparison.Ordinal));
    }

    [Fact]
    public void Trend_endpoint_short_circuits_before_alert_calculation_for_pending_restart()
    {
        var endpoint = Endpoint("app.MapGet(\"/api/sites/{id}/health/trend\"", "app.MapPost(\"/api/sites/{id}/health/alerts/acknowledge\"");

        Assert.Contains("registry.TryGetById(site.Id, out var active)", endpoint, StringComparison.Ordinal);
        Assert.Contains("if (SiteRuntimeState.IsPendingRestart(site, active))", endpoint, StringComparison.Ordinal);
        Assert.Contains("pendingRestart = true", endpoint, StringComparison.Ordinal);
        Assert.Contains("effectiveAlertState = \"pending-restart\"", endpoint, StringComparison.Ordinal);
        Assert.Contains("historyRetention = \"none\"", endpoint, StringComparison.Ordinal);
        Assert.True(endpoint.IndexOf("SiteRuntimeState.IsPendingRestart", StringComparison.Ordinal) < endpoint.IndexOf("var trend =", StringComparison.Ordinal));
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
