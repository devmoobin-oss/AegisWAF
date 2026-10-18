namespace AegisWAF.Client;

/// <summary>Plain-language (Thai) explanations of detection names and built-in rules.</summary>
public static class RuleText
{
    /// <summary>Plain-language meaning of a detection name shown in events.</summary>
    public static string Explain(string rule) => rule switch
    {
        "SQL Injection" => "พยายามแทรกคำสั่งเข้าฐานข้อมูล (SQL Injection)",
        "XSS" => "พยายามฝังสคริปต์อันตรายในหน้าเว็บ (XSS)",
        "Path Traversal" => "พยายามเข้าถึงไฟล์นอกโฟลเดอร์เว็บ (../)",
        "LFI/RFI" => "พยายามดึงไฟล์ในเครื่องหรือจากภายนอกผ่านตัวจัดการสตรีม (php://, file://)",
        "Command Injection" => "พยายามแทรกคำสั่งระบบปฏิบัติการ (เช่น ;cat, |id, $(...))",
        "SSRF" => "พยายามให้เซิร์ฟเวอร์ยิงไปที่อยู่ภายใน เช่น cloud metadata (169.254.169.254)",
        "NoSQL Injection" => "พยายามแทรก operator ของ NoSQL เช่น $ne, $gt, $where ({\"$ne\":null})",
        "XXE" => "พยายามใช้ external entity / DOCTYPE เพื่ออ่านไฟล์หรือยิงออกนอก (XML External Entity)",
        "Template Injection" => "พยายามแทรกนิพจน์ของ template engine ฝั่งเซิร์ฟเวอร์ (SSTI เช่น {{7*7}}, ${...})",
        "Anomaly Score" => "สัญญาณน่าสงสัยหลายอย่างรวมกันในค่าเดียวจนถึงเกณฑ์ (anomaly scoring)",
        "Malicious Scanner" => "User-Agent เป็นเครื่องมือสแกนช่องโหว่ (เช่น sqlmap, nikto)",
        "Protocol Anomaly" => "คำขอผิดปกติในระดับโปรโตคอล (null byte / แทรก CRLF)",
        "Response Inspection Timeout" => "ตรวจเนื้อหาขาออกไม่ทันเวลา จึงปฏิเสธเพื่อความปลอดภัย",
        _ when rule.StartsWith("Response Leak", StringComparison.Ordinal) => "พบข้อมูลอ่อนไหวรั่วในคำตอบจากเซิร์ฟเวอร์ปลายทาง จึงระงับไม่ให้ส่งออก (" + rule[(rule.IndexOf('—') + 1)..].Trim() + ")",
        "Rate Limit" => "ส่งคำขอถี่เกินกำหนดจาก IP เดียว",
        "IP Blocklist" => "IP นี้ถูกสั่งบล็อกไว้",
        "Inspection Body Limit" => "ข้อมูลที่ส่งมาใหญ่เกินกว่าที่ระบบตรวจได้",
        "Malformed Structured Body" => "รูปแบบ body ผิดรูปหรือพบ external entity ที่อาจใช้หลบการตรวจ",
        "Oversized URL" => "URL ยาวผิดปกติ",
        "Inspection Timeout" => "ตรวจไม่ทันเวลา จึงปฏิเสธเพื่อความปลอดภัย",
        _ when rule.StartsWith("Custom: ", StringComparison.Ordinal) => "ตรงกับกฎที่คุณสร้างเอง (" + rule[8..] + ")",
        _ => rule
    };

    public static string Describe(string id) => id switch
    {
        "sqli-01" => "ตรวจจับ UNION SELECT (ดึงข้อมูลข้ามตาราง) หลังถอดรหัส %xx และลบคอมเมนต์ /**/ แล้ว",
        "sqli-02" => "ตรวจจับเงื่อนไขบูลีน เช่น OR 1=1, ' OR '1'='1 ที่ใช้ข้ามการตรวจสอบสิทธิ์",
        "sqli-03" => "ตรวจจับ time-based (sleep/benchmark/waitfor), stacked query (;DROP) และการอ่าน metadata (information_schema, xp_cmdshell)",
        "xss-01" => "ตรวจจับ <script>, javascript:, vbscript:",
        "xss-02" => "ตรวจจับ event handler เช่น onerror=, onload=, onclick= และอื่นๆ อีกหลายสิบตัว",
        "xss-03" => "ตรวจจับแท็กอันตราย (<svg>, <img>, <iframe> ฯลฯ) และ sink เช่น document.cookie, data:text/html",
        "path-01" => "ตรวจจับการย้อนโฟลเดอร์ (../ หรือ ..\\) หลังถอดรหัส รวมถึงการเข้ารหัสซ้ำ",
        "path-02" => "ตรวจจับการอ้างไฟล์ระบบตรงๆ เช่น /etc/passwd, win.ini, web.config, .ssh/id_rsa",
        "lfi-01" => "ตรวจจับตัวจัดการสตรีม/สคีม เช่น php://, file://, data://, gopher://",
        "cmd-01" => "ตรวจจับตัวคั่นคำสั่ง (; | & ขึ้นบรรทัดใหม่) ตามด้วยคำสั่งระบบ เช่น cat, id, whoami, curl",
        "cmd-02" => "ตรวจจับการแทนค่าคำสั่ง $(...), ${...}, backtick และการเรียก /bin/sh, cmd.exe",
        "ssrf-01" => "ตรวจจับที่อยู่ cloud metadata และปลายทางภายใน (169.254.169.254 ฯลฯ)",
        "proto-01" => "ตรวจจับ null byte (%00) และการแทรกหัวข้อ HTTP (CRLF injection)",
        "nosql-01" => "ตรวจจับ operator ของ NoSQL เช่น $ne, $gt, $where, และรูปแบบ user[$ne]=",
        "xxe-01" => "ตรวจจับ XML External Entity / DOCTYPE และ SYSTEM/PUBLIC ที่อ้าง file://, http:// ฯลฯ",
        "ssti-01" => "ตรวจจับ template injection เช่น {{7*7}}, ${...}, <%...%>, #{...} และคีย์เวิร์ดอันตรายใน template",
        "scanner-01" => "ตรวจ User-Agent ว่าเป็นเครื่องมือสแกน/โจมตีอัตโนมัติ เช่น sqlmap, nikto, nuclei",
        "anomaly-01" => "ให้คะแนนสัญญาณน่าสงสัยหลายอย่าง เมื่อรวมกันถึงเกณฑ์จึงบล็อก (ปิดไว้โดยปริยาย เพราะอาจบล็อกผิดพลาดมากขึ้น)",
        _ => ""
    };
}
