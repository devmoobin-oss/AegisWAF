using System.Text;

namespace AegisWAF.Tests;

public sealed class HandoffLatestStateContractTests
{
    [Fact]
    public void Handoff_ends_with_the_latest_authoritative_pointer()
    {
        var root = FindRepoRoot();
        var handoff = File.ReadAllText(Path.Combine(root, "handoff.md"), Encoding.UTF8);
        const string marker = "### Current handoff pointer — 2026-10-28 (after Module 128)";
        var markerIndex = handoff.LastIndexOf(marker, StringComparison.Ordinal);

        Assert.True(markerIndex >= 0, "handoff.md must end with a current pointer.");
        var pointer = handoff[markerIndex..];
        Assert.Contains("Module 128 = 100%", pointer);
        Assert.Contains("Module 127 = 100%", pointer);
        Assert.Contains("Module 126 = 100%", pointer);
        Assert.Contains("Module 117 = 100%", pointer);
        Assert.Contains("Module 116 = 100%", pointer);
        Assert.Contains("Module 115 = 100%", pointer);
        Assert.Contains("Module 114 = 100%", pointer);
        Assert.Contains("Module 113 = 100%", pointer);
        Assert.Contains("Module 112 = 100%", pointer);
        Assert.Contains("Module 111 = 100%", pointer);
        Assert.Contains("Module 110 = 100%", pointer);
        Assert.Contains("Module 109 = 100%", pointer);
        Assert.Contains("Module 108 = 100%", pointer);
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
        Assert.Contains("571D6C14C5937DCD78D2B3CF61EA9F2404876C69A644E39273543F3C6C254CC3", pointer);
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
