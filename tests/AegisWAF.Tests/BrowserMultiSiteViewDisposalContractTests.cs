using System.Text;

namespace AegisWAF.Tests;

public sealed class BrowserMultiSiteViewDisposalContractTests
{
    [Fact]
    public void Browser_multi_site_disposes_its_view_when_navigation_leaves_the_page()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "wwwroot", "assets", "app.js"), Encoding.UTF8);
        var lifecycleStart = source.IndexOf("function disposeSiteView", StringComparison.Ordinal);
        var lifecycleEnd = source.IndexOf("async function loadSitesCrud", lifecycleStart, StringComparison.Ordinal);
        var wrapperStart = source.IndexOf("const loadSectionWithSiteCrud=loadSection", StringComparison.Ordinal);
        Assert.True(lifecycleStart >= 0 && lifecycleEnd > lifecycleStart && wrapperStart >= 0, "Could not locate the Multi-Site disposal lifecycle.");
        var lifecycle = source[lifecycleStart..lifecycleEnd];
        var wrapper = source[wrapperStart..];

        Assert.Contains("siteViewAbortController?.abort()", lifecycle, StringComparison.Ordinal);
        Assert.Contains("siteViewAbortController=null", lifecycle, StringComparison.Ordinal);
        Assert.Contains("siteViewGeneration++", lifecycle, StringComparison.Ordinal);
        Assert.Contains("if(active==='sites')", wrapper, StringComparison.Ordinal);
        Assert.Contains("disposeSiteView()", wrapper, StringComparison.Ordinal);
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
