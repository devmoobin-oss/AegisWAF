using System.Text;

namespace AegisWAF.Tests;

public sealed class BrowserMultiSiteDeleteSingleFlightContractTests
{
    [Fact]
    public void Browser_multi_site_delete_action_is_single_flight_per_site()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "wwwroot", "assets", "app.js"), Encoding.UTF8);
        var start = source.IndexOf("async function loadSitesCrud", StringComparison.Ordinal);
        var end = source.IndexOf("const loadSectionWithSiteCrud", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Could not isolate the browser Multi-Site workflow.");
        var sites = source[start..end];

        Assert.Contains("deleteInFlight=new Set()", sites, StringComparison.Ordinal);
        Assert.Contains("deleteInFlight.has(id)", sites, StringComparison.Ordinal);
        Assert.Contains("deleteInFlight.add(id)", sites, StringComparison.Ordinal);
        Assert.Contains("deleteInFlight.delete(id)", sites, StringComparison.Ordinal);
        Assert.Contains("remove.disabled=true", sites, StringComparison.Ordinal);
        Assert.Contains("if(remove.isConnected)remove.disabled=false", sites, StringComparison.Ordinal);
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
