using System.Text;

namespace AegisWAF.Tests;

public sealed class SiteHealthSummaryRuntimeTruthContractTests
{
    [Fact]
    public void Gateway_summary_marks_pending_sites_and_excludes_them_from_alert_counts()
    {
        var endpoint = Endpoint("app.MapGet(\"/api/sites/health/summary\"", "app.MapPost(\"/api/sites\"");

        Assert.Contains("SiteRegistry registry", endpoint, StringComparison.Ordinal);
        Assert.Contains("pendingSiteIds", endpoint, StringComparison.Ordinal);
        Assert.Contains("SiteRuntimeState.IsPendingRestart", endpoint, StringComparison.Ordinal);
        Assert.Contains("SiteHealthSummaryAlertCounts.Calculate(comparison.Where(item => !pendingSiteIds.Contains(item.SiteId)))", endpoint, StringComparison.Ordinal);
        Assert.Contains("pendingRestart = pendingRestart", endpoint, StringComparison.Ordinal);
        Assert.Contains("activationStatus", endpoint, StringComparison.Ordinal);
        Assert.Contains("pendingRestartSites = pendingSiteIds.Count", endpoint, StringComparison.Ordinal);
    }

    [Fact]
    public void Client_dtos_expose_pending_summary_state()
    {
        var models = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AegisWAF.Client", "Models.cs"), Encoding.UTF8);

        Assert.Contains("bool PendingRestart = false", models, StringComparison.Ordinal);
        Assert.Contains("string ActivationStatus = \"active\"", models, StringComparison.Ordinal);
        Assert.Contains("int PendingRestartSites = 0", models, StringComparison.Ordinal);
    }

    [Fact]
    public void Browser_summary_copy_explains_pending_runtime_sites()
    {
        var browser = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AegisWAF.Gateway", "wwwroot", "assets", "app.js"), Encoding.UTF8);

        Assert.Contains("pendingRestartSites:0", browser, StringComparison.Ordinal);
        Assert.Contains("pendingRestartSites", browser, StringComparison.Ordinal);
        Assert.Contains("รอเปิดใช้", browser, StringComparison.Ordinal);
    }

    [Fact]
    public void Desktop_summary_copy_explains_pending_runtime_sites()
    {
        var desktop = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AegisWAF.Desktop", "Views", "SitesView.xaml.cs"), Encoding.UTF8);

        Assert.Contains("PendingRestartSites", desktop, StringComparison.Ordinal);
        Assert.Contains("รอเปิดใช้", desktop, StringComparison.Ordinal);
    }

    private static string Endpoint(string startMarker, string endMarker)
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AegisWAF.Gateway", "GatewayApp.cs"), Encoding.UTF8);
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
