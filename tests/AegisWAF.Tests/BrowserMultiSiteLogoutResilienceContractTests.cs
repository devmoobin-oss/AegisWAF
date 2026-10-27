using System.Text;

namespace AegisWAF.Tests;

public sealed class BrowserMultiSiteLogoutResilienceContractTests
{
    [Fact]
    public void Explicit_logout_clears_local_session_and_disposes_multi_site_even_when_request_fails()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "wwwroot", "assets", "app.js"), Encoding.UTF8);
        var start = source.IndexOf("$('logoutBtn').addEventListener", StringComparison.Ordinal);
        var end = source.IndexOf("$('userBox').addEventListener", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Could not isolate the explicit logout handler.");
        var logout = source[start..end];

        Assert.Contains("try", logout, StringComparison.Ordinal);
        Assert.Contains("catch", logout, StringComparison.Ordinal);
        Assert.Contains("me=null", logout, StringComparison.Ordinal);
        Assert.Contains("disposeSiteView()", logout, StringComparison.Ordinal);
        Assert.Contains("showLogin()", logout, StringComparison.Ordinal);
        Assert.True(logout.IndexOf("me=null", StringComparison.Ordinal) < logout.IndexOf("showLogin()", StringComparison.Ordinal),
            "The local session must be cleared before showing the login screen.");
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
