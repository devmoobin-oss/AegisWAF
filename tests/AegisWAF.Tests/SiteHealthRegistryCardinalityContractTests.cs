namespace AegisWAF.Tests;

public sealed class SiteHealthRegistryCardinalityContractTests
{
    [Fact]
    public void Process_memory_health_registry_declares_a_global_site_cardinality_bound()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "Sites", "SiteHealth.cs"));

        Assert.Contains("maxSites", source, StringComparison.Ordinal);
        Assert.Contains("_maxSites", source, StringComparison.Ordinal);
        Assert.Contains("LastTouched", source, StringComparison.Ordinal);
        Assert.Contains("leastRecentlyUsed", source, StringComparison.Ordinal);
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
