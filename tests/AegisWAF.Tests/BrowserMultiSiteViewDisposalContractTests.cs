using System.Text;

namespace AegisWAF.Tests;

public sealed class BrowserMultiSiteViewDisposalContractTests
{
    [Fact]
    public void Browser_multi_site_disposes_its_view_when_navigation_leaves_the_page()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "wwwroot", "assets", "app.js"), Encoding.UTF8);
        var start = source.IndexOf("const loadSectionWithSiteCrud=loadSection", StringComparison.Ordinal);
        Assert.True(start >= 0, "Could not locate the Multi-Site section wrapper.");
        var wrapper = source[start..];

        Assert.Contains("if(active==='sites')", wrapper, StringComparison.Ordinal);
        Assert.Contains("siteViewAbortController?.abort()", wrapper, StringComparison.Ordinal);
        Assert.Contains("siteViewAbortController=null", wrapper, StringComparison.Ordinal);
        Assert.Contains("siteViewGeneration++", wrapper, StringComparison.Ordinal);
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
