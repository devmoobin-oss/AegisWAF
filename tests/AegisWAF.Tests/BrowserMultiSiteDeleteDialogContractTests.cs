using System.Text;

namespace AegisWAF.Tests;

public sealed class BrowserMultiSiteDeleteDialogContractTests
{
    [Fact]
    public void Browser_multi_site_delete_uses_an_accessible_app_owned_dialog()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "wwwroot", "assets", "app.js"), Encoding.UTF8);
        var start = source.IndexOf("if(active==='sites')", StringComparison.Ordinal);
        var end = source.IndexOf("const loadSectionWithSiteCrud", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Could not isolate the browser Multi-Site workflow.");
        var sites = source[start..end];

        var helperEnd = source.IndexOf("const titles=", StringComparison.Ordinal);
        Assert.True(helperEnd > 0, "Could not isolate the shared browser dialog helper.");
        var helpers = source[..helperEnd];

        Assert.Contains("function showAppDialog", helpers, StringComparison.Ordinal);
        Assert.Contains("setAttribute('role','alertdialog')", helpers, StringComparison.Ordinal);
        Assert.Contains("setAttribute('aria-modal','true')", helpers, StringComparison.Ordinal);
        Assert.Contains("aria-labelledby", helpers, StringComparison.Ordinal);
        Assert.Contains("Escape", helpers, StringComparison.Ordinal);
        Assert.Contains("focus()", helpers, StringComparison.Ordinal);
        Assert.Contains("showAppDialog", sites, StringComparison.Ordinal);
        Assert.Contains("data-site-delete", sites, StringComparison.Ordinal);
        Assert.DoesNotContain("function confirmSiteDelete", sites, StringComparison.Ordinal);
        Assert.DoesNotContain("confirm(`ลบ Site", sites, StringComparison.Ordinal);
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
