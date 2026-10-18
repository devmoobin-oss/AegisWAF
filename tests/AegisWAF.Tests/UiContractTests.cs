using System.Text;

namespace AegisWAF.Tests;

public sealed class UiContractTests
{
    [Fact]
    public void Browser_console_has_professional_feedback_and_accessibility_hooks()
    {
        var root = FindRepoRoot();
        var html = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "wwwroot", "index.html"), Encoding.UTF8);
        var css = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "wwwroot", "assets", "style.css"), Encoding.UTF8);
        var js = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "wwwroot", "assets", "app.js"), Encoding.UTF8);

        Assert.Contains("aria-live=\"polite\"", html);
        Assert.Contains("data-toast", html);
        Assert.Contains("prefers-reduced-motion", css);
        Assert.Contains("function toast", js);
        Assert.Contains("i-owasp", html);
        Assert.Contains("OWASP Top 10", html);
        Assert.Contains("data-owasp-filter", html);
        Assert.Contains("badge-legend", html);
        Assert.Contains("renderOwaspBadge", js);
        Assert.Contains("owaspCodes", js);
        Assert.Contains("ไม่ใช่การรับรอง", html + js);
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
