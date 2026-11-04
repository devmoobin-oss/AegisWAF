using System.Text;

namespace AegisWAF.Tests;

public sealed class SiteHealthAlertRuntimeGuardContractTests
{
    [Fact]
    public void Alert_acknowledgement_requires_an_active_runtime_snapshot()
    {
        var root = FindRepoRoot();
        var gateway = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "GatewayApp.cs"), Encoding.UTF8);
        var start = gateway.IndexOf("app.MapPost(\"/api/sites/{id}/health/alerts/acknowledge\"", StringComparison.Ordinal);
        var end = gateway.IndexOf("app.MapGet(\"/api/sites/health/summary\"", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Could not isolate the health alert acknowledgement endpoint.");
        var endpoint = gateway[start..end];
        var browser = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "wwwroot", "assets", "app.js"), Encoding.UTF8);

        Assert.Contains("SiteRegistry registry", endpoint, StringComparison.Ordinal);
        Assert.Contains("SiteRuntimeState.IsPendingRestart(site, active)", endpoint, StringComparison.Ordinal);
        Assert.Contains("site_runtime_pending_restart", endpoint, StringComparison.Ordinal);
        Assert.Contains("site_runtime_pending_restart", browser, StringComparison.Ordinal);
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
