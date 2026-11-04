using System.Text;

namespace AegisWAF.Tests;

public sealed class BrowserMultiSiteAsyncResilienceContractTests
{
    [Fact]
    public void Browser_multi_site_ignores_stale_navigation_results()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "wwwroot", "assets", "app.js"), Encoding.UTF8);
        var start = source.IndexOf("async function loadSitesCrud", StringComparison.Ordinal);
        var end = source.IndexOf("const loadSectionWithSiteCrud", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Could not isolate the browser Multi-Site workflow.");
        var sites = source[start..end];

        Assert.Contains("let siteViewGeneration=0", source, StringComparison.Ordinal);
        Assert.Contains("const viewGeneration=++siteViewGeneration", sites, StringComparison.Ordinal);
        Assert.Contains("const isCurrent=()=>active==='sites'&&siteViewGeneration===viewGeneration", sites, StringComparison.Ordinal);
        Assert.Contains("if(!isCurrent())return", sites, StringComparison.Ordinal);
        Assert.Contains("el.addEventListener('click',event=>{if(!isCurrent())return", sites, StringComparison.Ordinal);
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
