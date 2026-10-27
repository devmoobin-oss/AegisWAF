using System.Text;

namespace AegisWAF.Tests;

public sealed class SiteHealthTrendEndpointContractTests
{
    [Fact]
    public void Direct_health_trend_endpoint_exposes_and_uses_the_stale_state()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "GatewayApp.cs"), Encoding.UTF8);
        var start = source.IndexOf("app.MapGet(\"/api/sites/{id}/health/trend\"", StringComparison.Ordinal);
        var end = source.IndexOf("}).Require(Roles.Viewer);", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Could not isolate the direct health trend endpoint.");
        var endpoint = source[start..end];

        Assert.Contains("var trendIsStale = HealthAlertLifecycle.IsStale(trend, trendNow);", endpoint);
        Assert.Contains("HealthAlertLifecycle.IsActionable(trend, trendNow)", endpoint);
        Assert.Contains("isStale = trendIsStale", endpoint);
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
