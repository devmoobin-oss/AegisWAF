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
        Assert.Contains("Module 95 = 100%", pointer);
        Assert.Contains("Module 94 = 100%", pointer);
        Assert.Contains("Module 93 = 100%", pointer);
        Assert.Contains("Module 92 = 100%", pointer);
        Assert.Contains("Module 91 = 100%", pointer);
        Assert.Contains("Module 90 = 100%", pointer);
        Assert.Contains("Module 88 = 100%", pointer);
        Assert.Contains("Module 87 = 100%", pointer);
        Assert.Contains("Module 41 = 90% and Module 60 = 90%", pointer);
        Assert.Contains("5D3BFCAA070FFA06D4FFC99D8A0C9A7C012C9A8DC8CD5DEEED9EAA430BB7EB6F", pointer);
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
