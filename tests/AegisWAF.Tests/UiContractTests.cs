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
        Assert.Contains("site-activation-notice", js);
        Assert.Contains("เปิดใช้งาน route ทันที", js);
        Assert.DoesNotContain("ต้อง Restart โปรแกรมหรือ Gateway ก่อนจึงจะเริ่มใช้งาน route", js);
        Assert.Contains("pending-restart", js);
        Assert.Contains("วิกฤต", js);
        Assert.Contains("เฝ้าระวัง", js);
        Assert.Contains("ปกติ", js);
        Assert.Contains("liveHealth", js);
        Assert.Contains("historicalAlert", js);
        Assert.Contains("liveHealth==='healthy'", js);
        Assert.Contains("รอเปิดใช้", js);
        Assert.Contains("data-site-health", js);
        Assert.Contains("/health", js);
        Assert.Contains("0.10.2", js);
        Assert.Contains("Health Check enabled", js);
        Assert.Contains("siteUpstreams", js);
        Assert.Contains("id=url@weight", js);
        Assert.Contains("parseUpstreams", js);
        Assert.Contains("Upstreams:", js);
        Assert.Contains("weight", js);
        Assert.Contains("แต่ละบรรทัด", js);
        Assert.Contains("siteHealthPath", js);
        Assert.Contains("siteHealthInterval", js);
        Assert.Contains("siteHealthTimeout", js);
        Assert.Contains("siteHealthPassive", js);
        Assert.Contains("siteRateRequests", js);
        Assert.Contains("siteRateWindow", js);
        Assert.Contains("siteRateGlobal", js);
        Assert.Contains("Health:", js);
        Assert.Contains("RateLimit:", js);
        Assert.Contains("healthTimeout>=healthInterval", js);
        Assert.Contains("เส้นทาง Health", js);
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

    [Fact]
    public void Desktop_theme_defines_dynamic_resources_used_by_owasp_view()
    {
        var root = FindRepoRoot();
        var theme = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Desktop", "Theme.xaml"), Encoding.UTF8);
        foreach (var key in new[] { "IconFont", "SignFont", "MonoFont", "LineGreen", "Amber", "Slate", "Faint" })
            Assert.Contains($"x:Key=\"{key}\"", theme);
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
        Assert.Contains("GetSiteHealthSummaryAsync", code);
        Assert.Contains("AcknowledgeSiteHealthAlertAsync", code);
        Assert.Contains("Sites.ToDictionary", code);
        Assert.DoesNotContain("GetSiteHealthTrendAsync", code);
        Assert.Contains("Trend", xaml);
        Assert.Contains("AlertText", code);
        Assert.Contains("SummaryText", xaml);
        Assert.Contains("OnAcknowledgeAlert", xaml + code);
        Assert.Contains("กำลังโหลดภาพรวม health trend", code);
        Assert.Contains("summary.QueryDurationMs", code);
        Assert.Contains("summary.WithinBudget", code);
        Assert.Contains("RetrySummaryButton", xaml + code);
        Assert.Contains("OnRetrySummary", xaml + code);
        Assert.Contains("RetrySummaryButton.IsEnabled = false", code);
        Assert.Contains("Health trend summary unavailable", code);
        Assert.Contains("SiteHealthSummaryRecoveryState", code);
        Assert.Contains("ข้อมูลล่าสุดอาจ stale", code);
        Assert.Contains("RecoveryEventsText", xaml + code);
        Assert.Contains("RecoveryDetailsExpander", xaml + code);
        Assert.Contains("IsFreshnessStale", code);
        Assert.Contains("LastSuccessfulSummaryAge", code);
        Assert.Contains("Keyboard", xaml + code);
        Assert.Contains("CopyRecoveryDiagnosticsButton", xaml + code);
        Assert.Contains("OnCopyRecoveryDiagnostics", xaml + code);
        Assert.Contains("CopyRecoveryTimelineButton", xaml + code);
        Assert.Contains("OnCopyRecoveryTimeline", xaml + code);
        Assert.Contains("SiteHealthSummaryTimelineExporter.Export", code);
        Assert.Contains("BeginRefresh", code);
        Assert.Contains("BuildDiagnostics", code);
        Assert.Contains("RecoveryDetailsExpander.Visibility = Visibility.Collapsed", code);
        Assert.Contains("CopyRecoveryDiagnosticsButton.IsEnabled = false", code);
        Assert.Contains("CopyRecoveryTimelineButton.IsEnabled = false", code);
        Assert.Contains("DiagnosticsAvailable", code);
        Assert.Contains("SiteHealthSummaryRefreshLifecycle", code);
        Assert.Contains("RecoveryState", code);
        Assert.Contains("recovered", code);
        Assert.Contains("กลับมาปกติ", code);
        Assert.Contains("RecoveredSites", code);
        Assert.Contains("StaleCriticalSites", code);
        Assert.Contains("StaleDegradedSites", code);
        Assert.Contains("วิกฤติปัจจุบัน", code);
        Assert.Contains("ข้อมูลเก่า", code);
        Assert.Contains("SiteHealthDisplayState.Resolve", code);
        Assert.Contains("DisplayState", code);
        Assert.Contains("กลับมาปกติ · มีประวัติเก่า", code);
        Assert.Contains("pending-restart", code);
        Assert.Contains("health trend:", code);
        Assert.Contains("สถานะ runtime ดูในแถว Site", code);
        Assert.Contains("OnUnloaded", xaml + code);
        Assert.Contains("OperationCanceledException", code);
        Assert.Contains("GetSitesAsync(ct)", code);
        Assert.Contains("GetSiteHealthSummaryAsync(ct: ct)", code);
        Assert.Contains("ct.ThrowIfCancellationRequested", code);
        Assert.Contains("_refreshLifecycle.Invalidate(RefreshCancellationReason.Unloaded)", code);
        Assert.Contains("CurrentGeneration", code);
        Assert.Contains("IsCurrentGeneration", code);
        Assert.Contains("_refreshLifecycle.Invalidate", code);
        Assert.Contains("RefreshCancellationReason.Retry", code);
        Assert.Contains("RefreshCancellationReason.Unloaded", code);
        Assert.Contains("_refreshLifecycle.Complete", code);
        Assert.Contains("CancellationReasonFilter", xaml + code);
        Assert.Contains("OnCancellationReasonFilterChanged", xaml + code);
        Assert.Contains("AutomationProperties.Name", xaml);
        Assert.Contains("GetCancellationHistory", code);
        Assert.Contains("TakeLast(3)", code);
        Assert.Contains("item.Sequence", code);
        Assert.DoesNotContain("OrderBy(item => item.OccurredAt)", code);
        Assert.Contains("RetrySummaryButton.IsEnabled = false", code);
        Assert.Contains("ลองใหม่", xaml);
    }

    [Fact]
    public void Desktop_multi_site_form_exposes_a_reviewed_development_private_exception()
    {
        var root = FindRepoRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Desktop", "Views", "SitesView.xaml"), Encoding.UTF8);
        var code = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Desktop", "Views", "SitesView.xaml.cs"), Encoding.UTF8);
        var models = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Client", "Models.cs"), Encoding.UTF8);

        Assert.Contains("AllowPrivateExceptionsBox", xaml + code);
        Assert.Contains("ทดสอบเท่านั้น", xaml);
        Assert.Contains("AllowPrivateExceptions", code + models);
        Assert.Contains("Reason", code + models);
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
