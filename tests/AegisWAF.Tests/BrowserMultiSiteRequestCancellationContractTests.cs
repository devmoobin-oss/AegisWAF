using System.Text;

namespace AegisWAF.Tests;

public sealed class BrowserMultiSiteRequestCancellationContractTests
{
    [Fact]
    public void Browser_multi_site_binds_all_view_requests_to_the_view_abort_signal()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "wwwroot", "assets", "app.js"), Encoding.UTF8);
        var start = source.IndexOf("async function loadSitesCrud", StringComparison.Ordinal);
        var end = source.IndexOf("const loadSectionWithSiteCrud", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Could not isolate the browser Multi-Site workflow.");
        var sites = source[start..end];

        Assert.True(sites.Split("signal:eventController.signal", StringSplitOptions.None).Length - 1 >= 5,
            "Site list, health summary, health probe, mutation, and delete requests must all use the view abort signal.");
        Assert.Contains("authFetch('/api/sites',{cache:'no-store',signal:eventController.signal})", sites, StringComparison.Ordinal);
        Assert.Contains("/api/sites/health/summary?window=20&limit=100',{cache:'no-store',signal:eventController.signal}", sites, StringComparison.Ordinal);
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
