using System.Text;
using System.Text.Json;

namespace AegisWAF.Tests;

public sealed class WebUiOwnershipContractTests
{
    [Fact]
    public void Web_ui_declares_native_select_ownership_and_canonical_rows()
    {
        var root = FindRepoRoot();
        var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "premium-ui.json"), Encoding.UTF8));
        Assert.Equal("product-admin", manifest.RootElement.GetProperty("profile").GetString());
        Assert.Equal("native", manifest.RootElement.GetProperty("ownership").GetProperty("Select/Listbox").GetString());

        var contract = File.ReadAllText(Path.Combine(root, "UX-CONTRACT.md"), Encoding.UTF8);
        foreach (var capability in new[] { "Table Selection", "Select/Listbox", "Form", "Scrollbar", "Toast", "CRUD" })
            Assert.Contains($"| {capability} |", contract, StringComparison.Ordinal);
    }

    [Fact]
    public void Application_forms_use_explicit_application_validation()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AegisWAF.Gateway", "wwwroot", "index.html"), Encoding.UTF8);

        Assert.Contains("id=\"loginForm\" class=\"auth-card\" autocomplete=\"on\" novalidate", source, StringComparison.Ordinal);
        Assert.Contains("id=\"changeForm\" class=\"auth-card\" novalidate", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Browser_scrollbar_has_standards_properties_and_webkit_fallback()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AegisWAF.Gateway", "wwwroot", "assets", "style.css"), Encoding.UTF8);

        Assert.Contains("scrollbar-color:", source, StringComparison.Ordinal);
        Assert.Contains("scrollbar-width:", source, StringComparison.Ordinal);
        Assert.Contains("::-webkit-scrollbar", source, StringComparison.Ordinal);
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
