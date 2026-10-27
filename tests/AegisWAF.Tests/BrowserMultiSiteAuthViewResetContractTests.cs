using System.Text;

namespace AegisWAF.Tests;

public sealed class BrowserMultiSiteAuthViewResetContractTests
{
    [Fact]
    public void Login_transition_resets_the_protected_section_before_exposing_login()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "wwwroot", "assets", "app.js"), Encoding.UTF8);
        var start = source.IndexOf("function showLogin()", StringComparison.Ordinal);
        var end = source.IndexOf("function showChange", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Could not isolate the login transition.");
        var login = source[start..end];

        Assert.Contains("page('dashboard')", login, StringComparison.Ordinal);
        Assert.Contains("$('sectionContent').replaceChildren()", login, StringComparison.Ordinal);
        Assert.True(login.IndexOf("$('sectionContent').replaceChildren()", StringComparison.Ordinal) < login.IndexOf("$('loginOverlay').hidden=false", StringComparison.Ordinal),
            "Protected section content must be cleared before the login overlay is exposed.");
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
