using System.Text;

namespace AegisWAF.Tests;

public sealed class DesktopDynamicResourceScopeContractTests
{
    [Fact]
    public void Dynamic_desktop_views_resolve_application_level_styles_from_application_scope()
    {
        var root = FindRepoRoot();
        foreach (var file in new[] { "DashboardView.xaml.cs", "ProtectionView.xaml.cs" })
        {
            var view = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Desktop", "Views", file), Encoding.UTF8);

            Assert.DoesNotContain("= (Style)FindResource(\"", view, StringComparison.Ordinal);
            Assert.Contains("Application.Current.FindResource(\"", view, StringComparison.Ordinal);
        }
    }

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "AegisWAF.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
