using System.Text;

namespace AegisWAF.Tests;

public sealed class BrowserMultiSiteTableSemanticsContractTests
{
    [Fact]
    public void Browser_multi_site_table_names_each_row_action_with_its_site()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "wwwroot", "assets", "app.js"), Encoding.UTF8);
        var start = source.IndexOf("async function loadSitesCrud", StringComparison.Ordinal);
        var end = source.IndexOf("const loadSectionWithSiteCrud", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Could not isolate the browser Multi-Site workflow.");
        var sites = source[start..end];

        Assert.Contains("<caption class=\"sr-only\">รายการ Site และสถานะระบบ</caption>", sites, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"ตรวจสอบ Health ของ ${escapeHtml(site.siteId)}\"", sites, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"แก้ไข Site ${escapeHtml(site.siteId)}\"", sites, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"ลบ Site ${escapeHtml(site.siteId)}\"", sites, StringComparison.Ordinal);
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
