using System.Text;

namespace AegisWAF.Tests;

public sealed class SiteEndpointReadBudgetTests
{
    [Fact]
    public void Site_list_and_manual_health_reads_use_the_bounded_budget()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "GatewayApp.cs"), Encoding.UTF8);

        var listEndpoint = Endpoint(source, "app.MapGet(\"/api/sites\"", "app.MapPost(\"/api/sites/{id}/health\"");
        var manualHealthEndpoint = Endpoint(source, "app.MapPost(\"/api/sites/{id}/health\"", "app.MapGet(\"/api/sites/{id}/health/history\"");

        foreach (var endpoint in new[] { listEndpoint, manualHealthEndpoint })
        {
            Assert.Contains("using var readBudget = SiteHealthReadBudget.Create(ct);", endpoint, StringComparison.Ordinal);
            Assert.Contains("var readCt = readBudget.Token;", endpoint, StringComparison.Ordinal);
            Assert.Contains("store.ListAsync(readCt)", endpoint, StringComparison.Ordinal);
            Assert.Contains("SiteHealthReadBudget.IsBudgetCancellation(ct, readCt)", endpoint, StringComparison.Ordinal);
            Assert.Contains("site_health_read_timeout", endpoint, StringComparison.Ordinal);
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
