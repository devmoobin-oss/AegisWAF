using System.Text;

namespace AegisWAF.Tests;

public sealed class DesktopOwaspResourceScopeContractTests
{
    [Fact]
    public void Owasp_view_resolves_application_level_font_resources_from_application_scope()
    {
        var root = FindRepoRoot();
        var view = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Desktop", "Views", "OwaspCoverageView.xaml.cs"), Encoding.UTF8);

        Assert.Contains("Application.Current.FindResource(\"IconFont\")", view, StringComparison.Ordinal);
        Assert.DoesNotContain("FontFamily = (FontFamily)FindResource(\"IconFont\")", view, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "AegisWAF.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
