using System.Text;

namespace AegisWAF.Tests;

public sealed class SiteManagerContractTests
{
    [Fact]
    public void Desktop_site_manager_exposes_health_owasp_and_restart_state()
    {
        var root = FindRepoRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Desktop", "Views", "SitesView.xaml"), Encoding.UTF8);

        Assert.Contains("SiteId", xaml, StringComparison.Ordinal);
        Assert.Contains("Health", xaml, StringComparison.Ordinal);
        Assert.Contains("OWASP", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("restart", xaml, StringComparison.OrdinalIgnoreCase);
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
