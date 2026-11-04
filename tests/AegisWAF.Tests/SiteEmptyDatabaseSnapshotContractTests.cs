using System.Text;

namespace AegisWAF.Tests;

public sealed class SiteEmptyDatabaseSnapshotContractTests
{
    [Fact]
    public void Empty_database_result_is_materialized_as_an_authoritative_snapshot()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(
            Path.Combine(root, "src", "AegisWAF.Gateway", "Sites", "SiteDatabaseStore.cs"),
            Encoding.UTF8);

        Assert.DoesNotContain("return sites.Count == 0 ? null", source, StringComparison.Ordinal);
        Assert.Contains("return new SiteConfigurationSnapshot(sites, false);", source, StringComparison.Ordinal);
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
