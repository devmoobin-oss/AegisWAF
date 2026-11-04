namespace AegisWAF.Client;

public enum CoverageState { Covered, Partial, NotCovered }

public sealed record OwaspCategory(string Code, string Title, CoverageState Coverage, string Description, string Limitations);
public sealed record RuleSecurityMetadata(IReadOnlyList<string> Codes, CoverageState Coverage, string Description, string Limitations);

/// <summary>Single source of truth for OWASP Top 10:2021 mapping shown by the gateway and its clients.</summary>
public static class OwaspCatalog
{
    public const string OfficialUrl = "https://top10.owasp.org/2021/";
    public static IReadOnlyList<OwaspCategory> All { get; } =
    [
        new("A01", "Broken Access Control", CoverageState.Partial, "สิทธิ์และการเข้าถึงที่ไม่ควรถูกอนุญาต", "WAF ช่วยได้เฉพาะ route/admin/IP policy; authorization ของแอปปลายทางต้องตรวจเอง"),
        new("A02", "Cryptographic Failures", CoverageState.NotCovered, "การปกป้องข้อมูลและการเข้ารหัสที่ไม่เพียงพอ", "ต้องใช้ TLS, secret management และ cryptographic storage ที่แอป/โครงสร้างพื้นฐาน"),
        new("A03", "Injection", CoverageState.Partial, "การแทรกคำสั่งหรือ expression ผ่าน input", "เป็น signature/normalization defense ไม่แทน parameterized query หรือ output encoding"),
        new("A04", "Insecure Design", CoverageState.NotCovered, "ข้อบกพร่องจากการออกแบบระบบ", "ต้องใช้ threat modeling และ secure design review"),
        new("A05", "Security Misconfiguration", CoverageState.Partial, "การตั้งค่าความปลอดภัยที่ไม่เหมาะสม", "ครอบคลุมเฉพาะ policy ของ gateway/admin response และ request limits"),
        new("A06", "Vulnerable and Outdated Components", CoverageState.NotCovered, "ส่วนประกอบที่มีช่องโหว่หรือเก่าเกินไป", "ต้องใช้ dependency inventory และ SCA/patch management"),
        new("A07", "Identification and Authentication Failures", CoverageState.Partial, "การยืนยันตัวตนและเซสชันที่อ่อนแอ", "ครอบคลุม auth throttling, lockout, session และ audit ของ admin เท่านั้น"),
        new("A08", "Software and Data Integrity Failures", CoverageState.Partial, "ความสมบูรณ์ของซอฟต์แวร์/ข้อมูลและการเปลี่ยนแปลง", "ครอบคลุม audit/config persistence บางส่วน ไม่ใช่ supply-chain verification"),
        new("A09", "Security Logging and Monitoring Failures", CoverageState.Partial, "การบันทึกและติดตามเหตุการณ์ไม่เพียงพอ", "บันทึก detection/audit ใน gateway แต่ต้องมี retention/alerting ภายนอก"),
        new("A10", "Server-Side Request Forgery (SSRF)", CoverageState.Partial, "การบังคับให้เซิร์ฟเวอร์ยิงไปยังปลายทางที่ไม่ควรเข้าถึง", "guard ไม่ resolve DNS และไม่แทนที่ outbound allowlist ของแอปปลายทาง")
    ];

    private static readonly IReadOnlyDictionary<string, string[]> RuleCodes = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["sqli-01"] = ["A03"], ["sqli-02"] = ["A03"], ["sqli-03"] = ["A03"], ["xss-01"] = ["A03"], ["xss-02"] = ["A03"], ["xss-03"] = ["A03"],
        ["path-01"] = ["A03"], ["path-02"] = ["A03"], ["lfi-01"] = ["A03", "A10"], ["cmd-01"] = ["A03"], ["cmd-02"] = ["A03"], ["ssrf-01"] = ["A10"],
        ["proto-01"] = ["A03", "A05"], ["nosql-01"] = ["A03"], ["xxe-01"] = ["A03", "A10"], ["ssti-01"] = ["A03"], ["scanner-01"] = ["A06"], ["anomaly-01"] = ["A03"],
        ["ipblock"] = ["A01"], ["ratelimit"] = ["A07", "A09"], ["admin"] = ["A01", "A07", "A09"], ["bodytypes"] = ["A03", "A05"], ["bodysize"] = ["A05"], ["response"] = ["A09"], ["custom"] = ["A03"], ["other"] = [],
        ["SQL Injection"] = ["A03"], ["XSS"] = ["A03"], ["Path Traversal"] = ["A03"], ["LFI/RFI"] = ["A03", "A10"],
        ["Command Injection"] = ["A03"], ["NoSQL Injection"] = ["A03"], ["XXE"] = ["A03", "A10"], ["Template Injection"] = ["A03"],
        ["Malicious Scanner"] = ["A06"], ["Protocol Anomaly"] = ["A03", "A05"], ["Anomaly Score"] = ["A03"]
    };

    public static OwaspCategory ForCode(string code) => All.FirstOrDefault(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase))
        ?? new OwaspCategory(code.ToUpperInvariant(), "ไม่ระบุหมวด OWASP", CoverageState.NotCovered, "ยังไม่มี mapping ที่ยืนยัน", "ต้องตรวจสอบ mapping ก่อนอ้างอิง");

    public static RuleSecurityMetadata ForRule(string ruleId)
    {
        if (ruleId.Equals("SSRF", StringComparison.OrdinalIgnoreCase) || ruleId.StartsWith("SSRF:", StringComparison.OrdinalIgnoreCase)) return ForRule("ssrf-01");
        if (ruleId.Equals("Rate Limit", StringComparison.OrdinalIgnoreCase)) return ForRule("ratelimit");
        if (ruleId.Equals("IP Blocklist", StringComparison.OrdinalIgnoreCase)) return ForRule("ipblock");
        if (ruleId.StartsWith("Response ", StringComparison.OrdinalIgnoreCase)) return ForRule("response");
        var codes = RuleCodes.TryGetValue(ruleId, out var mapped) ? mapped : Array.Empty<string>();
        var categories = codes.Select(ForCode).ToArray();
        var coverage = categories.Length == 0 ? CoverageState.NotCovered : categories.All(x => x.Coverage == CoverageState.Covered) ? CoverageState.Covered : CoverageState.Partial;
        return new RuleSecurityMetadata(codes, coverage,
            categories.Length == 0 ? "กฎนี้ยังไม่มี OWASP mapping ที่ยืนยัน" : string.Join(" / ", categories.Select(x => x.Description)),
            categories.Length == 0 ? "อย่าใช้ชื่อกฎเพื่อเดา OWASP category" : string.Join(" / ", categories.Select(x => x.Limitations).Distinct()));
    }

    public static IReadOnlyList<string> RuleIdsForCode(string code) => RuleCodes.Where(x => x.Value.Contains(code, StringComparer.OrdinalIgnoreCase)).Select(x => x.Key).OrderBy(x => x).ToArray();
}
