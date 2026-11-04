using System.Text;

namespace AegisWAF.Tests;

public sealed class SiteListRuntimeLookupContractTests
{
    [Fact]
    public void Sites_list_uses_a_case_insensitive_runtime_index_instead_of_nested_scans()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AegisWAF.Gateway", "GatewayApp.cs"), Encoding.UTF8);
        var start = source.IndexOf("app.MapGet(\"/api/sites\"", StringComparison.Ordinal);
        var end = source.IndexOf("app.MapPost(\"/api/sites/{id}/health\"", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Could not isolate the Sites list endpoint.");
        var endpoint = source[start..end];

        Assert.Contains("activeById", endpoint, StringComparison.Ordinal);
        Assert.Contains("activeById.TryGetValue(site.Id, out var active)", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("activeSites.FirstOrDefault", endpoint, StringComparison.Ordinal);
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
