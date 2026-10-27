using System.Text;
using AegisWAF.Gateway.Sites;

namespace AegisWAF.Tests;

public sealed class SiteHealthEffectiveStateTests
{
    [Fact]
    public void Recovered_history_is_exposed_as_recovered_not_active_critical()
    {
        var trend = Trend("critical", "recovered");

        Assert.Equal("recovered", HealthAlertLifecycle.EffectiveAlertState(trend, isStale: false));
    }

    [Fact]
    public void Stale_history_is_exposed_as_stale_even_when_raw_alert_is_critical()
    {
        var trend = Trend("critical", "none");

        Assert.Equal("stale", HealthAlertLifecycle.EffectiveAlertState(trend, isStale: true));
    }

    [Fact]
    public void Current_critical_history_remains_critical()
    {
        var trend = Trend("critical", "none");

        Assert.Equal("critical", HealthAlertLifecycle.EffectiveAlertState(trend, isStale: false));
    }

    [Fact]
    public void Current_healthy_history_remains_healthy()
    {
        var trend = Trend("healthy", "none");

        Assert.Equal("healthy", HealthAlertLifecycle.EffectiveAlertState(trend, isStale: false));
    }

    [Fact]
    public void Health_endpoints_contractually_expose_the_normalized_state()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "GatewayApp.cs"), Encoding.UTF8);

        var trendStart = source.IndexOf("app.MapGet(\"/api/sites/{id}/health/trend\"", StringComparison.Ordinal);
        var trendEnd = source.IndexOf("}).Require(Roles.Viewer);", trendStart, StringComparison.Ordinal);
        Assert.True(trendStart >= 0 && trendEnd > trendStart);
        Assert.Contains("effectiveAlertState = HealthAlertLifecycle.EffectiveAlertState(trend, trendIsStale)", source[trendStart..trendEnd], StringComparison.Ordinal);

        var summaryStart = source.IndexOf("app.MapGet(\"/api/sites/health/summary\"", StringComparison.Ordinal);
        var summaryEnd = source.IndexOf("app.MapPost(\"/api/sites", summaryStart, StringComparison.Ordinal);
        Assert.True(summaryStart >= 0 && summaryEnd > summaryStart);
        Assert.Contains("effectiveAlertState = HealthAlertLifecycle.EffectiveAlertState(item)", source[summaryStart..summaryEnd], StringComparison.Ordinal);

        var acknowledgeStart = source.IndexOf("app.MapPost(\"/api/sites/{id}/health/alerts/acknowledge\"", StringComparison.Ordinal);
        var acknowledgeEnd = source.IndexOf("app.MapGet(\"/api/sites/health/summary\"", acknowledgeStart, StringComparison.Ordinal);
        Assert.True(acknowledgeStart >= 0 && acknowledgeEnd > acknowledgeStart);
        Assert.Contains("effectiveAlertState = HealthAlertLifecycle.EffectiveAlertState(trend, trendIsStale)", source[acknowledgeStart..acknowledgeEnd], StringComparison.Ordinal);
    }

    private static SiteHealthTrend Trend(string alertState, string recoveryState) =>
        new(20, 3, 0, 3, 0, 0, 3, alertState, DateTimeOffset.UtcNow, recoveryState);

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
