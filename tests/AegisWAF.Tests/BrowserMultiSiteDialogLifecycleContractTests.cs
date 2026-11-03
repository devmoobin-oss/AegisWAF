using System.Text;

namespace AegisWAF.Tests;

public sealed class BrowserMultiSiteDialogLifecycleContractTests
{
    [Fact]
    public void Browser_multi_site_delete_dialog_closes_when_the_view_is_aborted()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "wwwroot", "assets", "app.js"), Encoding.UTF8);
        var start = source.IndexOf("function showAppDialog", StringComparison.Ordinal);
        var roleEnd = source.IndexOf("const titles=", start, StringComparison.Ordinal);
        var sitesStart = source.IndexOf("async function loadSitesCrud", StringComparison.Ordinal);
        var sitesEnd = source.IndexOf("const loadSectionWithSiteCrud", sitesStart, StringComparison.Ordinal);
        Assert.True(start >= 0 && roleEnd > start && sitesStart >= 0 && sitesEnd > sitesStart, "Could not isolate the browser dialog and Multi-Site workflows.");
        var dialog = source[start..roleEnd];
        var sites = source[sitesStart..sitesEnd];

        Assert.Contains("signal", dialog, StringComparison.Ordinal);
        Assert.Contains("const onAbort=()=>finish(false,true)", dialog, StringComparison.Ordinal);
        Assert.Contains("signal?.addEventListener('abort',onAbort", dialog, StringComparison.Ordinal);
        Assert.Contains("signal?.removeEventListener('abort',onAbort", dialog, StringComparison.Ordinal);
        Assert.Contains("showAppDialog({title:'ยืนยันการลบ Site'", sites, StringComparison.Ordinal);
        Assert.Contains("signal:eventController.signal", sites, StringComparison.Ordinal);
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
