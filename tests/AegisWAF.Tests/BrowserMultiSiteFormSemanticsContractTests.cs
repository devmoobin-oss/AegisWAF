using System.Text;

namespace AegisWAF.Tests;

public sealed class BrowserMultiSiteFormSemanticsContractTests
{
    [Fact]
    public void Browser_multi_site_form_exposes_labels_and_shared_help_to_assistive_technology()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "wwwroot", "assets", "app.js"), Encoding.UTF8);
        var start = source.IndexOf("async function loadSitesCrud", StringComparison.Ordinal);
        var end = source.IndexOf("const loadSectionWithSiteCrud", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Could not isolate the browser Multi-Site workflow.");
        var sites = source[start..end];

        Assert.Contains("id=\"site-form-title\"", sites, StringComparison.Ordinal);
        Assert.Contains(">เพิ่ม Site</h2>", sites, StringComparison.Ordinal);
        Assert.Contains("aria-labelledby=\"site-form-title\"", sites, StringComparison.Ordinal);
        Assert.Contains("aria-describedby=\"site-form-help\"", sites, StringComparison.Ordinal);
        Assert.Contains("id=\"site-form-help\"", sites, StringComparison.Ordinal);
        foreach (var field in new[] { "siteId", "siteHosts", "sitePath", "siteUpstreams", "siteEgress", "sitePolicy", "siteHealthPath", "siteHealthInterval", "siteHealthTimeout", "siteHealthPassive", "siteRateRequests", "siteRateWindow", "siteRateGlobal" })
        {
            Assert.Contains($"for=\"{field}\"", sites, StringComparison.Ordinal);
        }

        Assert.Contains("class=\"site-field\"", sites, StringComparison.Ordinal);
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
