using System.Text;
using System.Text.RegularExpressions;

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

    [Fact]
    public void Desktop_application_resources_do_not_reference_missing_local_types()
    {
        var root = FindRepoRoot();
        var desktopRoot = Path.Combine(root, "src", "AegisWAF.Desktop");
        var app = File.ReadAllText(Path.Combine(desktopRoot, "App.xaml"), Encoding.UTF8);
        var source = string.Join("\n", Directory.EnumerateFiles(desktopRoot, "*.cs", SearchOption.AllDirectories)
            .Select(path => File.ReadAllText(path, Encoding.UTF8)));

        var resourceTypes = Regex.Matches(app, "<local:(?<type>[A-Za-z_][A-Za-z0-9_]*)\\b")
            .Select(match => match.Groups["type"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        foreach (var type in resourceTypes)
            Assert.Matches($"\\b(class|record|struct)\\s+{Regex.Escape(type)}\\b", source);
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
