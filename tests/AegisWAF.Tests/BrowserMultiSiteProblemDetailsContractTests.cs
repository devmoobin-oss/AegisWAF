using System.Text;

namespace AegisWAF.Tests;

public sealed class BrowserMultiSiteProblemDetailsContractTests
{
    [Fact]
    public void Browser_multi_site_actions_render_rfc7807_diagnostics_without_trace_details()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "wwwroot", "assets", "app.js"), Encoding.UTF8);
        var start = source.IndexOf("if(active==='sites')", StringComparison.Ordinal);
        var end = source.IndexOf("const loadSectionWithSiteCrud", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Could not isolate the browser Multi-Site workflow.");
        var sites = source[start..end];

        Assert.Contains("function readApiError", source, StringComparison.Ordinal);
        Assert.Contains("detail.title", source, StringComparison.Ordinal);
        Assert.Contains("detail.detail", source, StringComparison.Ordinal);
        Assert.Contains("novalidate", sites, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("resize:none", sites, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("await readApiError(response", sites, StringComparison.Ordinal);
        Assert.DoesNotContain("detail.error||`HTTP ${response.status}`", sites, StringComparison.Ordinal);
        Assert.DoesNotContain("traceId", source[source.IndexOf("function readApiError", StringComparison.Ordinal)..source.IndexOf("const titles=", StringComparison.Ordinal)], StringComparison.Ordinal);
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
