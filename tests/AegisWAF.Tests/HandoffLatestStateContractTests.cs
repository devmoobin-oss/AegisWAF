using System.Text;

namespace AegisWAF.Tests;

public sealed class HandoffLatestStateContractTests
{
    [Fact]
    public void Handoff_ends_with_the_latest_authoritative_pointer()
    {
        var root = FindRepoRoot();
        var handoff = File.ReadAllText(Path.Combine(root, "handoff.md"), Encoding.UTF8);
        const string marker = "### Current handoff pointer — 2026-10-27";
        var markerIndex = handoff.LastIndexOf(marker, StringComparison.Ordinal);

        Assert.True(markerIndex >= 0, "handoff.md must end with a current pointer.");
        var pointer = handoff[markerIndex..];
        Assert.Contains("Module 90 = 100%", pointer);
        Assert.Contains("Module 88 = 100%", pointer);
        Assert.Contains("Module 87 = 100%", pointer);
        Assert.Contains("Module 41 = 90% and Module 60 = 90%", pointer);
        Assert.Contains("5F632D2B9D8C7EDA063FBB4CE1ABA610FBF34F87D5BB539E149A19D8C4CFC466", pointer);
        Assert.Contains("D:\\AegisWAF\\dist\\AegisWAF-Desktop-CompletedModules\\AegisWAF.exe", pointer);
        Assert.Equal(markerIndex, handoff.LastIndexOf(marker, StringComparison.Ordinal));
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
