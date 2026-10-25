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
        Assert.Contains("/assets/app.js?v=", html);
        Assert.Contains("data-toast", html);
        Assert.Contains("prefers-reduced-motion", css);
        Assert.Contains("function toast", js);
        Assert.Contains("i-owasp", html);
        Assert.Contains("OWASP Top 10", html);
        Assert.Contains("data-owasp-filter", html);
        Assert.Contains("badge-legend", html);
        Assert.Contains("renderOwaspBadge", js);
        Assert.Contains("owaspCodes", js);
        Assert.Contains("/api/sites", js);
        Assert.Contains("method:'POST'", js);
        Assert.Contains("method:'PUT'", js);
        Assert.Contains("method:'DELETE'", js);
        Assert.Contains("site-restart-warning", js);
        Assert.Contains("Restart โปรแกรมหรือ Gateway", js);
        Assert.Contains("pending-restart", js);
        Assert.Contains("data-site-health", js);
        Assert.Contains("/health", js);
        Assert.Contains("0.10.1", js);
        Assert.Contains("Health Check enabled", js);
        Assert.Contains("ไม่ใช่การรับรอง", html + js);
    }

    [Fact]
    public void Desktop_multi_site_table_exposes_health_check_action()
    {
        var root = FindRepoRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Desktop", "Views", "SitesView.xaml"), Encoding.UTF8);
        var code = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Desktop", "Views", "SitesView.xaml.cs"), Encoding.UTF8);
        var client = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Client", "ApiClient.cs"), Encoding.UTF8);

        Assert.Contains("เช็ค Healthy", xaml);
        Assert.Contains("Click=\"OnHealthCheck\"", xaml);
        Assert.Contains("x:Name=\"VersionText\"", xaml);
        Assert.Contains("OnHealthCheck", code);
        Assert.Contains("VersionText.Text", code);
        Assert.Contains("AegisWAF v", code);
        Assert.Contains("api/sites/{Uri.EscapeDataString(id)}/health", client);
    }

    [Fact]
    public void Desktop_command_center_exposes_live_security_traffic_contract()
    {
        var root = FindRepoRoot();
        var main = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Desktop", "MainWindow.xaml"), Encoding.UTF8);
        var code = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Desktop", "MainWindow.xaml.cs"), Encoding.UTF8);
        var view = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Desktop", "Views", "CommandCenterView.xaml"), Encoding.UTF8);
        var viewCode = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Desktop", "Views", "CommandCenterView.xaml.cs"), Encoding.UTF8);

        Assert.Contains("Security Command Center", main + code + view);
        Assert.Contains("CommandCenterView", code + viewCode);
        Assert.Contains("Active connections", view);
        Assert.Contains("Requests/min", view);
        Assert.Contains("Client IP", view);
        Assert.Contains("Forwarded to", view);
        Assert.Contains("Decision", view);
        Assert.Contains("OWASP", view);
        Assert.Contains("GetCommandCenterTrafficAsync", viewCode);
    }

    [Fact]
    public void Desktop_command_center_ignores_filter_events_during_xaml_initialization()
    {
        var root = FindRepoRoot();
        var viewCode = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Desktop", "Views", "CommandCenterView.xaml.cs"), Encoding.UTF8);

        Assert.Contains("private bool _initialized;", viewCode);
        Assert.Contains("_initialized = true;", viewCode);
        Assert.Contains("if (!_initialized) return;", viewCode);
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
