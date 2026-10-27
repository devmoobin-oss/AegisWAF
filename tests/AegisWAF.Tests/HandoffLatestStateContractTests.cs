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
        Assert.Contains("Module 107 = 100%", pointer);
        Assert.Contains("Module 106 = 100%", pointer);
        Assert.Contains("Module 105 = 100%", pointer);
        Assert.Contains("Module 104 = 100%", pointer);
        Assert.Contains("Module 103 = 100%", pointer);
        Assert.Contains("Module 102 = 100%", pointer);
        Assert.Contains("Module 101 = 100%", pointer);
        Assert.Contains("Module 100 = 100%", pointer);
        Assert.Contains("Module 99 = 100%", pointer);
        Assert.Contains("Module 98 = 100%", pointer);
        Assert.Contains("Module 97 = 100%", pointer);
        Assert.Contains("Module 95 = 100%", pointer);
        Assert.Contains("Module 94 = 100%", pointer);
        Assert.Contains("Module 93 = 100%", pointer);
        Assert.Contains("Module 92 = 100%", pointer);
        Assert.Contains("Module 91 = 100%", pointer);
        Assert.Contains("Module 90 = 100%", pointer);
        Assert.Contains("Module 88 = 100%", pointer);
        Assert.Contains("Module 87 = 100%", pointer);
        Assert.Contains("Module 41 = 90% and Module 60 = 90%", pointer);
        Assert.Contains("4A54823E1C3B0DBCCAE4C5A245E26679396D5C3FB43BD5877104B69BD0E13110", pointer);
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
