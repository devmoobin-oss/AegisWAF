using System.Text;

namespace AegisWAF.Tests;

public sealed class BrowserSecurityActionDialogContractTests
{
    [Fact]
    public void Browser_security_actions_use_the_shared_accessible_dialog()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "wwwroot", "assets", "app.js"), Encoding.UTF8);
        var rulesStart = source.IndexOf("if(active==='rules')", StringComparison.Ordinal);
        var ipsStart = source.IndexOf("if(active==='ips')", StringComparison.Ordinal);
        var sitesStart = source.IndexOf("if(active==='sites')", StringComparison.Ordinal);
        Assert.True(rulesStart >= 0 && ipsStart > rulesStart && sitesStart > ipsStart, "Could not isolate the rules and IP policy workflows.");

        var helpers = source[..source.IndexOf("const ROLE_LEVEL", StringComparison.Ordinal)];
        var rules = source[rulesStart..ipsStart];
        var ips = source[ipsStart..sitesStart];

        Assert.Contains("function showAppDialog", helpers, StringComparison.Ordinal);
        Assert.Contains("role','alertdialog", helpers, StringComparison.Ordinal);
        Assert.Contains("aria-modal", helpers, StringComparison.Ordinal);
        Assert.Contains("aria-labelledby", helpers, StringComparison.Ordinal);
        Assert.Contains("aria-describedby", helpers, StringComparison.Ordinal);
        Assert.Contains("event.key==='Escape'", helpers, StringComparison.Ordinal);
        Assert.Contains("event.key!=='Tab'", helpers, StringComparison.Ordinal);
        Assert.Contains("previous&&typeof previous.focus==='function'", helpers, StringComparison.Ordinal);
        Assert.Contains("confirmButton.disabled=value", helpers, StringComparison.Ordinal);
        Assert.Contains("setPending(false);finish(true)", helpers, StringComparison.Ordinal);
        Assert.Contains("readApiError(", rules, StringComparison.Ordinal);
        Assert.Contains("readApiError(", ips, StringComparison.Ordinal);
        Assert.Contains("await loadSection();setSectionStatus('ipActionStatus'", ips, StringComparison.Ordinal);
        Assert.DoesNotContain("confirm(", rules, StringComparison.Ordinal);
        Assert.DoesNotContain("alert(", rules, StringComparison.Ordinal);
        Assert.DoesNotContain("confirm(", ips, StringComparison.Ordinal);
        Assert.DoesNotContain("alert(", ips, StringComparison.Ordinal);
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
