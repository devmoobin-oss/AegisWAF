using System.Text;

namespace AegisWAF.Tests;

public sealed class BrowserMultiSiteAuthInvalidationContractTests
{
    [Fact]
    public void Auth_expiry_disposes_the_active_multi_site_view_before_showing_login()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "wwwroot", "assets", "app.js"), Encoding.UTF8);
        var authStart = source.IndexOf("function authFetch", StringComparison.Ordinal);
        var authEnd = source.IndexOf("async function readApiError", authStart, StringComparison.Ordinal);
        Assert.True(authStart >= 0 && authEnd > authStart, "Could not isolate authFetch.");
        var authFetch = source[authStart..authEnd];

        Assert.Contains("if(r.status===401&&me)", authFetch, StringComparison.Ordinal);
        Assert.Contains("disposeSiteView()", authFetch, StringComparison.Ordinal);
        Assert.True(authFetch.IndexOf("disposeSiteView()", StringComparison.Ordinal) < authFetch.IndexOf("showLogin()", StringComparison.Ordinal),
            "The Multi-Site view must be disposed before the login screen is shown.");

        var lifecycleStart = source.IndexOf("let siteViewGeneration", StringComparison.Ordinal);
        var lifecycleEnd = source.IndexOf("async function loadSitesCrud", lifecycleStart, StringComparison.Ordinal);
        Assert.True(lifecycleStart >= 0 && lifecycleEnd > lifecycleStart, "Could not isolate the Multi-Site lifecycle helpers.");
        var lifecycle = source[lifecycleStart..lifecycleEnd];
        Assert.Contains("function disposeSiteView", lifecycle, StringComparison.Ordinal);
        Assert.Contains("siteViewAbortController?.abort()", lifecycle, StringComparison.Ordinal);
        Assert.Contains("siteViewAbortController=null", lifecycle, StringComparison.Ordinal);
        Assert.Contains("siteViewGeneration++", lifecycle, StringComparison.Ordinal);
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
