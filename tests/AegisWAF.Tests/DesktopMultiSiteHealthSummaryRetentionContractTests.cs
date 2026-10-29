using System.Text;

namespace AegisWAF.Tests;

public sealed class DesktopMultiSiteHealthSummaryRetentionContractTests
{
    [Fact]
    public void Desktop_multi_site_explains_when_summary_health_is_process_memory_only()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Desktop", "Views", "SitesView.xaml.cs"), Encoding.UTF8);

        Assert.Contains("summary.HistoryPersisted", source, StringComparison.Ordinal);
        Assert.Contains("ข้อมูล Health ยังอยู่ในหน่วยความจำชั่วคราว", source, StringComparison.Ordinal);
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
