# AegisWAF Layered OWASP Defense & Coverage UI

## Objective

เพิ่มความสามารถการป้องกันของ AegisWAF ให้เป็น layered defense ที่ตรวจสอบได้จริง พร้อมแสดงความสัมพันธ์ระหว่างกฎกับ OWASP Top 10:2021 อย่างโปร่งใส โดยไม่อ้างว่า WAF เพียงตัวเดียวทำให้แอปพลิเคชัน compliant หรือปลอดภัยครบทุกหมวด

## Scope

### In scope

- เพิ่ม OWASP metadata ให้ built-in rules และ protection catalog
- เพิ่ม endpoint สำหรับ coverage matrix และ rule metadata
- เพิ่ม badge/icon `A01`–`A10` ในหน้า WAF Rules, Protection และ security events
- เพิ่ม login throttling ต่อ IP และป้องกัน brute-force ใน auth flow
- เพิ่ม security headers สำหรับ admin console และ response policy ที่ไม่ทำลาย protected application
- เพิ่ม SSRF guard แบบ parse URL/scheme/IP จริงในค่าที่ถูกตรวจ โดย block loopback, private, link-local, cloud metadata และ scheme อันตราย
- เพิ่ม OWASP code และ detection reason ใน event/audit payload ที่เหมาะสม
- เพิ่ม regression tests สำหรับ allow/block, false positive ที่สำคัญ, auth throttling, security headers และ mapping
- คง API เดิมที่ผู้ใช้ใช้งานอยู่ให้ backward-compatible เมื่อทำได้

### Out of scope

- ไม่ทำให้ AegisWAF เป็น OWASP CRS หรือ full-featured commercial WAF
- ไม่อ้างว่าการมี badge หมายถึงผ่าน OWASP assessment
- ไม่เพิ่มการ decode/decrypt TLS ใน gateway เอง
- ไม่ทำ vulnerability scanner สำหรับ dependency หรือ source code
- ไม่เปลี่ยน policy ของ upstream application โดยอัตโนมัติ
- ไม่เพิ่มการบล็อกแบบ heuristic ที่เปิดใช้งานทันทีโดยไม่มี false-positive controls

## OWASP mapping policy

ใช้ OWASP Top 10:2021 เป็น taxonomy ทางการ:

| Code | Category | Runtime WAF posture |
|---|---|---|
| A01 | Broken Access Control | ครอบคลุมบางส่วนผ่าน admin RBAC, IP policies และ route gating |
| A02 | Cryptographic Failures | ไม่ใช่สิ่งที่ signature WAF แก้ได้; แสดงเป็น platform responsibility |
| A03 | Injection | ครอบคลุมผ่าน SQLi, NoSQL, command, SSTI, XXE และ custom rules บางส่วน |
| A04 | Insecure Design | ไม่สามารถยืนยันจาก request filtering; แสดงเป็น not covered by WAF |
| A05 | Security Misconfiguration | เสริม security headers, safe defaults, request limits และ explicit status |
| A06 | Vulnerable and Outdated Components | ต้องใช้ dependency/SCA tooling; ไม่ให้ WAF อ้างว่าครอบคลุม |
| A07 | Identification and Authentication Failures | เสริม login throttling, lockout visibility, session/audit controls |
| A08 | Software and Data Integrity Failures | จำกัด scope เป็น audit/config integrity; ไม่อ้างว่าตรวจ supply chain |
| A09 | Security Logging and Monitoring Failures | เพิ่ม detection reason, OWASP code และ correlation fields ใน events |
| A10 | SSRF | เสริม parser-based scheme/IP guard และคง signature detection เป็นชั้นสำรอง |

Mapping ต้องอยู่ใน source of truth เดียว ไม่ให้ UI เดา category จากชื่อ rule เอง

## Proposed architecture

### 1. Protection metadata

เพิ่ม model สำหรับ rule metadata ที่มี `RuleId`, `OwaspCodes`, `Coverage`, `Description` และ `Limitations` โดย built-in rules, anomaly rule และ protection catalog ใช้ mapping เดียวกัน

`RuleDto` และ security event DTO จะเพิ่ม field ใหม่แบบ nullable/optional เพื่อไม่ทำให้ client เก่าพัง:

- `owaspCodes: string[]`
- `coverage: Covered | Partial | NotCovered`
- `reason` หรือ `explanation` ตามบริบท

### 2. SSRF guard

สร้าง guard แยกจาก regex engine เพื่อวิเคราะห์ candidate URL ใน query, form, JSON/text body, headers และ cookies:

- ยอมรับเฉพาะ scheme ที่กำหนดใน policy
- block `file`, `gopher`, `dict`, `ftp`, `data`, `php` และ scheme ที่ไม่ปลอดภัยตามค่าเริ่มต้น
- parse IPv4, IPv6, hostname และ DNS-independent special ranges
- block loopback, unspecified, link-local, private/reserved และ cloud metadata addresses
- ไม่ resolve DNS เพื่อหลีกเลี่ยง network side effects ใน WAF path; การป้องกัน DNS rebinding ต้องทำที่ outbound HTTP client ของ upstream
- return stable reason เช่น `SSRF: private target` เพื่อให้ event อธิบายได้

### 3. Authentication throttling

เพิ่ม limiter เฉพาะ auth endpoints แยกจาก traffic limiter:

- key เป็น normalized client IP + username เมื่อมี username
- จำกัด failed attempts ใน time window และตอบข้อความทั่วไปไม่เปิดเผยว่าบัญชีมีอยู่จริง
- รักษา account lockout เดิมและไม่ bypass ด้วยการเปลี่ยน username casing
- ไม่ lockout ทั้งระบบจาก IP เดียวโดยไม่มี upper bound
- เขียน audit event สำหรับ throttle/lockout outcome

### 4. Security headers

เพิ่ม middleware เฉพาะ admin UI/API responses ที่ gateway เป็นเจ้าของ:

- `X-Content-Type-Options: nosniff`
- `Referrer-Policy: no-referrer`
- `Content-Security-Policy` ที่เข้ากับ inline SVG/JS ของ console โดยลด unsafe directives เท่าที่ทำได้
- `Cache-Control: no-store` สำหรับ login/session/admin API
- `Permissions-Policy` แบบปิด capability ที่ไม่จำเป็น

ไม่ inject CSP หรือ cache policy แบบกว้างใส่ upstream response โดยไม่แยก route

### 5. Coverage API and UI

เพิ่ม endpoint read-only `/api/owasp-coverage` สำหรับผู้ใช้ที่มีสิทธิ์ดูระบบ โดยส่ง:

- รายการ A01–A10
- coverage state
- rule IDs ที่เชื่อมโยง
- limitations และ responsibility note
- link ไป OWASP official page แบบ external/documentation metadata เท่านั้น

หน้า UI จะเพิ่ม:

- icon shield + code badge ในแต่ละ rule
- legend ว่า badge หมายถึง mapping ไม่ใช่ certification
- filter ตาม OWASP code และ coverage state
- event detail แสดง rule, OWASP code, action และ reason
- empty/partial state ที่บอกสิ่งที่ผู้ดูแลต้องทำต่อ

## Error handling and safety

- Guard failures ที่เกิดจาก malformed input จะ block เฉพาะเมื่อมีเหตุผลชัดเจน ไม่ทำให้ request ปกติล่มทั้ง pipeline
- เมื่อ parser/inspection timeout ให้ fail closed ตาม policy ปัจจุบันและบันทึก `Inspection Timeout`
- SSRF guard ต้องมี unit tests สำหรับ IPv4, IPv6, hostname, encoded URL, userinfo, alternate ports และ malformed URI
- หลีกเลี่ยงการส่ง payload จริงไปยัง external host ใน test suite
- event/audit ต้องไม่เก็บ password, session token, CSRF token หรือ request body ทั้งก้อน
- security headers ต้องทดสอบว่าไม่ถูกเติมให้ binary/stream response ของ upstream โดยไม่จำเป็น

## Testing strategy

เพิ่ม tests ก่อน implementation สำหรับ:

1. OWASP mapping ของทุก built-in rule และ catalog item
2. SSRF guard allow/block matrix และ false-positive cases
3. Login throttling, casing normalization, lockout และ audit outcome
4. Admin security headers และ no-store policy
5. Coverage API schema และ backward compatibility ของ existing DTOs
6. UI contract: badge/icon, coverage legend, filter hooks และ accessible labels
7. Full existing gateway/catalog verification suite

เมื่อ environment พร้อม ให้รัน `dotnet test` เต็มชุด รวม PostgreSQL integration tests และตรวจด้วย Impeccable detector หลังแก้ UI

## Files expected to change

- `src/AegisWAF.Gateway/Program.cs` หรือแยกไฟล์ guard/policy ตาม boundary ที่เหมาะสม
- `src/AegisWAF.Gateway/Auth/AuthEndpoints.cs` และ auth support classes
- `src/AegisWAF.Gateway/GatewayApp.cs`
- `src/AegisWAF.Client/Models.cs`, `ProtectionCatalog.cs`, `RuleText.cs`
- `src/AegisWAF.Gateway/wwwroot/index.html`
- `src/AegisWAF.Gateway/wwwroot/assets/app.js`
- `src/AegisWAF.Gateway/wwwroot/assets/style.css`
- `tests/AegisWAF.Tests/*` ที่เกี่ยวข้อง
- `docs/PROTECTION.md` และ documentation ที่อธิบาย coverage

## Acceptance criteria

- ทุก rule ที่แสดงใน UI มี OWASP mapping หรือระบุ `Not mapped` อย่างชัดเจน
- A01–A10 แสดง coverage และ limitation โดยไม่อ้าง compliance
- payload SSRF ไปยัง loopback/private/link-local/metadata และ dangerous schemes ถูก block โดยไม่ต้องทำ outbound request
- failed login ที่ถี่เกิน policy ถูก throttle และมี audit record โดยไม่เปิดเผย account existence
- admin responses มี security headers ตาม policy และ session/admin responses ไม่ถูก cache
- existing protection catalog tests และ authentication/authorization tests ยังผ่าน
- UI แสดง badge/icon พร้อม accessible label และยังใช้งานได้บน mobile

