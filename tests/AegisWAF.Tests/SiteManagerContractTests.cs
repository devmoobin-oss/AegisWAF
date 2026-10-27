using System.Text;

namespace AegisWAF.Tests;

public sealed class SiteManagerContractTests
{
    [Fact]
    public void Desktop_site_manager_exposes_health_owasp_and_live_activation_state()
    {
        var root = FindRepoRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Desktop", "Views", "SitesView.xaml"), Encoding.UTF8);

        Assert.Contains("SiteId", xaml, StringComparison.Ordinal);
        Assert.Contains("Health", xaml, StringComparison.Ordinal);
        Assert.Contains("OWASP", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ใช้งานทันที", xaml, StringComparison.Ordinal);
        Assert.Contains("AddSiteButton", xaml, StringComparison.Ordinal);
        Assert.Contains("OnAddSite", xaml, StringComparison.Ordinal);
        Assert.Contains("OnEditSite", xaml, StringComparison.Ordinal);
        Assert.Contains("OnDeleteSite", xaml, StringComparison.Ordinal);
        Assert.Contains("UpstreamsBox", xaml, StringComparison.Ordinal);
        Assert.Contains("HealthPathBox", xaml, StringComparison.Ordinal);
        Assert.Contains("RateRequestsBox", xaml, StringComparison.Ordinal);
        foreach (var thaiFieldExplanation in new[]
        {
            "รหัส Site",
            "ชื่อโฮสต์ที่ผู้ใช้เรียก",
            "เส้นทาง URL ที่เปิดให้บริการ",
            "ปลายทางเว็บจริง",
            "โฮสต์ปลายทางที่อนุญาต",
            "นโยบาย WAF",
            "ปลายทางสำรองเพิ่มเติม",
            "เส้นทางตรวจสุขภาพ",
            "จำกัดคำขอ",
            "จำกัดคำขอรวมทุก Site",
            "Hostname ต้องไม่ซ้ำกับ Site อื่น"
        })
            Assert.Contains(thaiFieldExplanation, xaml, StringComparison.Ordinal);
        var gateway = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "GatewayApp.cs"), Encoding.UTF8);
        var codeBehind = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Desktop", "Views", "SitesView.xaml.cs"), Encoding.UTF8);
        var readiness = File.ReadAllText(Path.Combine(root, "docs", "PRODUCTION_READINESS.md"), Encoding.UTF8);
        var security = File.ReadAllText(Path.Combine(root, "docs", "SECURITY.md"), Encoding.UTF8);
        Assert.Contains("source = \"yarp-runtime-state\"", gateway, StringComparison.Ordinal);
        Assert.Contains("source = \"active-probe\"", gateway, StringComparison.Ordinal);
        Assert.Contains("requiresRestart = false", gateway, StringComparison.Ordinal);
        Assert.Contains("active = true", gateway, StringComparison.Ordinal);
        Assert.DoesNotContain("Restart Gateway", xaml + gateway, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("route ใช้งานทันที", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("require Gateway restart", codeBehind, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("activates a valid committed route without Gateway restart", readiness, StringComparison.Ordinal);
        Assert.DoesNotContain("active runtime configuration remains immutable until Gateway restart", readiness, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SHA-256 `prev_hash`/`entry_hash` chain", security, StringComparison.Ordinal);
        Assert.DoesNotContain("| 15 | Audit log is append-only by convention, not tamper-evident | Low | **Open**", security, StringComparison.Ordinal);
        Assert.Contains("lastProbeAt", gateway, StringComparison.Ordinal);
        Assert.Contains("Multi-Site Gateway", File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Desktop", "Views", "SettingsView.xaml")), StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"Upstream\"", File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Desktop", "Views", "SettingsView.xaml")), StringComparison.Ordinal);
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
