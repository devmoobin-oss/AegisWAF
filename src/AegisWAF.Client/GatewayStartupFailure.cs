namespace AegisWAF.Client;

/// <summary>Converts common embedded-Gateway startup failures into actionable operator guidance.</summary>
public static class GatewayStartupFailure
{
    public static string Describe(string? message, int? listenPort = null)
    {
        var original = string.IsNullOrWhiteSpace(message) ? "ไม่ทราบสาเหตุ" : message.Trim();
        var lower = original.ToLowerInvariant();
        var detail = $" รายละเอียดเดิม: {original}";

        if (lower.Contains("ux_waf_site_hostnames_active_hostname", StringComparison.Ordinal) ||
            (lower.Contains("could not create unique index", StringComparison.Ordinal) && lower.Contains("hostname", StringComparison.Ordinal)))
            return "เริ่มระบบไม่สำเร็จ: Hostname ของ Site ซ้ำกันในฐานข้อมูล — ตรวจสอบหรือลบ/แก้ไข Site ที่ใช้ Hostname ซ้ำ แล้วกดลองใหม่" + detail;

        if (lower.Contains("address already in use", StringComparison.Ordinal) ||
            lower.Contains("only one usage", StringComparison.Ordinal))
        {
            var port = listenPort is { } value ? $"{value}" : "ที่กำหนด";
            return $"เริ่มระบบไม่สำเร็จ: พอร์ต {port} ถูกโปรแกรมอื่นใช้อยู่ — ปิดโปรแกรมที่ใช้พอร์ตนี้หรือกดตั้งค่าใหม่เพื่อเปลี่ยนพอร์ต" + detail;
        }

        if (lower.Contains("password authentication failed", StringComparison.Ordinal) ||
            lower.Contains("connection refused", StringComparison.Ordinal) ||
            lower.Contains("actively refused", StringComparison.Ordinal) ||
            lower.Contains("postgres", StringComparison.Ordinal) && lower.Contains("connect", StringComparison.Ordinal))
            return "เริ่มระบบไม่สำเร็จ: เชื่อมต่อ PostgreSQL ไม่ได้ — ตรวจสอบว่า PostgreSQL ทำงานอยู่และรหัสผ่านใน Connection String ถูกต้อง" + detail;

        return original;
    }
}
