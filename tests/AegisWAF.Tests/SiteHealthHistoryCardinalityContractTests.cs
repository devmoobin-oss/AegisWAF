using System.Text;

namespace AegisWAF.Tests;

public sealed class SiteHealthHistoryCardinalityContractTests
{
    [Fact]
    public void Process_memory_health_history_declares_a_global_site_cardinality_bound()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AegisWAF.Gateway", "Sites", "SiteHealthHistory.cs"), Encoding.UTF8);

        Assert.Contains("maxSites", source, StringComparison.Ordinal);
        Assert.Contains("_maxSites", source, StringComparison.Ordinal);
        Assert.Contains("LastTouched", source, StringComparison.Ordinal);
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
