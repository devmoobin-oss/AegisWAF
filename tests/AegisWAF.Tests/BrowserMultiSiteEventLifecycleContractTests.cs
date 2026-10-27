using System.Text;

namespace AegisWAF.Tests;

public sealed class BrowserMultiSiteEventLifecycleContractTests
{
    [Fact]
    public void Browser_multi_site_aborts_the_previous_view_event_lifecycle()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "wwwroot", "assets", "app.js"), Encoding.UTF8);
        var start = source.IndexOf("async function loadSitesCrud", StringComparison.Ordinal);
        var end = source.IndexOf("const loadSectionWithSiteCrud", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Could not isolate the browser Multi-Site workflow.");
        var sites = source[start..end];

        Assert.Contains("siteViewAbortController=null", source, StringComparison.Ordinal);
        Assert.Contains("siteViewAbortController?.abort()", sites, StringComparison.Ordinal);
        Assert.Contains("const eventController=new AbortController()", sites, StringComparison.Ordinal);
        Assert.Contains("siteViewAbortController=eventController", sites, StringComparison.Ordinal);
        Assert.Contains("{signal:eventController.signal}", sites, StringComparison.Ordinal);
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
