using AegisWAF.Client;

namespace AegisWAF.Tests;

public sealed class GatewayStartupFailureTests
{
    [Fact]
    public void Duplicate_hostname_migration_failure_is_actionable_and_keeps_original_detail()
    {
        var message = GatewayStartupFailure.Describe(
            "23505: could not create unique index \"ux_waf_site_hostnames_active_hostname\"", 5080);

        Assert.Contains("Hostname ของ Site ซ้ำกัน", message, StringComparison.Ordinal);
        Assert.Contains("ตรวจสอบหรือลบ/แก้ไข Site ที่ใช้ Hostname ซ้ำ", message, StringComparison.Ordinal);
        Assert.Contains("23505", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_startup_failure_preserves_original_message()
    {
        const string original = "migration checksum mismatch";

        Assert.Equal(original, GatewayStartupFailure.Describe(original));
    }
}
