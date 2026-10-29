using System.Text;

namespace AegisWAF.Tests;

public sealed class SiteCrudProgressiveLoadContractTests
{
    [Fact]
    public void Sites_list_renders_before_health_summary_request_completes()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AegisWAF.Gateway", "wwwroot", "assets", "app.js"), Encoding.UTF8);
        var start = source.IndexOf("  const load=async()=>", StringComparison.Ordinal);
        var end = source.IndexOf("  el.innerHTML=", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Could not isolate Multi-Site load workflow.");
        var load = source[start..end];
        var baseResponse = load.IndexOf("authFetch('/api/sites'", StringComparison.Ordinal);
        var initialRender = load.IndexOf("render(items);", StringComparison.Ordinal);
        var summaryRequest = load.IndexOf("authFetch('/api/sites/health/summary", StringComparison.Ordinal);

        Assert.True(baseResponse >= 0, "Base Site list request is missing.");
        Assert.True(initialRender > baseResponse, "Base Site list must render after its response.");
        Assert.True(summaryRequest > initialRender, "Health summary should load after the base list is rendered.");
        Assert.Contains("summaryLoadState='loading'", load, StringComparison.Ordinal);
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
