# Security Command Center Design

Date: 2026-10-19  
Status: Design approved; implementation pending

## Goal

เพิ่มเมนู Desktop ชื่อ `Security Command Center` สำหรับผู้ปฏิบัติงานด้านความปลอดภัย เพื่อดูภาพรวม traffic ล่าสุดและตอบคำถามได้ทันทีว่าใครเชื่อมต่อเข้ามา, เข้า Site ใด, ถูกส่งต่อไปยัง upstream ใด, request ผ่านหรือถูกป้องกัน, และเหตุผลด้านความปลอดภัยคืออะไร

หน้าใหม่นี้ต้องช่วยตรวจสอบการทำงานของ reverse proxy และ WAF โดยไม่เปิดเผยข้อมูลลับหรือทำให้ทุก request ต้องถูกเขียนลง PostgreSQL แบบ synchronous

## Scope

### Included

- Gateway telemetry สำหรับ request ที่ผ่าน management pipeline และ reverse proxy
- Bounded in-memory ring buffer สำหรับ traffic ล่าสุด
- Summary endpoint และ traffic endpoint ที่ป้องกันด้วย session/RBAC เดิม
- Desktop menu/view ใหม่ที่ refresh ทุก 2 วินาที
- การแสดง site, hostname, client IP, method, path ที่ redact แล้ว, upstream, status, action, rule/OWASP, latency และ trace ID
- การแยกสถานะ `ALLOWED`, `BLOCKED`, `RATE LIMITED`, `UPSTREAM ERROR` และ `UNKNOWN`
- Unit/integration/UI contract tests ที่ตรวจ security boundary และข้อมูลสำคัญ

### Explicitly excluded

- ไม่เก็บ request body, response body, Cookie, Authorization, session token หรือ raw query string
- ไม่สร้าง WebSocket/SSE ในรุ่นแรก; ใช้ polling ที่มี limit เพื่อให้เข้ากับ Desktop client เดิม
- ไม่เขียนทุก allowed request ลง audit table ใน PostgreSQL
- ไม่ให้หน้า command center ใช้แทน SIEM, long-term retention หรือ incident case management
- ไม่เพิ่ม action ที่เปลี่ยน policy จากหน้า command centerในรุ่นแรก

## User experience

เพิ่ม navigation item ใหม่ระหว่าง Dashboard และ Events:

`Security Command Center`

หน้าประกอบด้วย:

1. Summary cards: Active connections, Requests/min, Allowed, Blocked, Rate limited, Upstream errors
2. Live traffic table เรียงจากใหม่ไปเก่า โดยมีคอลัมน์:
   - Time
   - Client IP
   - Site / Hostname
   - Method / Path
   - Forwarded to (upstream ID/address)
   - Decision
   - HTTP status
   - Latency
   - Security reason / OWASP badge
3. Filter ตาม IP, site, hostname, decision, status และข้อความค้นหา
4. Notice ระบุว่าเป็น live bounded view และข้อมูลเก่าจะถูกหมุนออกตาม retention
5. Empty, loading, offline และ permission-denied states

สีของ decision ต้องสื่อความหมายชัดเจน: allowed = green, blocked/rate limited = red/orange, upstream error = amber, unknown = muted

## Gateway data flow

สร้าง `TrafficTelemetry` singleton ที่มี bounded ring buffer และ counters โดยกำหนด capacity จาก `Waf:Telemetry:LiveCapacity` (ค่าเริ่มต้น 500) และไม่อนุญาตให้ configuration ทำให้ buffer โตไม่จำกัด

ทุก request ที่เป็น protected site request จะสร้าง record หนึ่งรายการเมื่อ request จบหรือถูก short-circuit:

- `Timestamp`
- `TraceId`
- `ClientIp` จาก trusted client address resolver
- `Hostname` และ `SiteId` จาก `SiteContext` ถ้ามี
- `Method`
- `Path` ที่ตัด query string ออก หรือเก็บเฉพาะ query key ที่ allowlist (รุ่นแรกให้ตัดทั้งหมด)
- `UpstreamId` และ sanitized `UpstreamAddress` จาก site configuration เมื่อ route ถูกเลือก
- `Decision`
- `StatusCode`
- `Rule`/`Reason`
- OWASP codes ที่คำนวณจาก rule
- `DurationMs`

การบันทึก telemetry ต้องไม่ทำให้ request ล้มเหลวและต้องไม่รอ database/network logging หาก buffer เต็ม ให้ทิ้งข้อมูลเก่าตาม ring-buffer policy และเพิ่ม dropped counter

Active connections ให้ใช้ counter แบบ increment/decrement รอบ protected request; ต้องป้องกันค่าติดลบเมื่อ request ถูกยกเลิกหรือ middleware หลายชั้นจบซ้ำ

## API contract

เพิ่ม endpoint ภายใต้ `/api/command-center`:

### `GET /api/command-center/summary`

สิทธิ์ขั้นต่ำ `SECURITY_ANALYST` และต้องผ่าน `AdminGuard` session policy

Response:

```json
{
  "generatedAt": "2026-10-19T12:00:00Z",
  "activeConnections": 2,
  "requestsPerMinute": 42,
  "allowed": 37,
  "blocked": 3,
  "rateLimited": 1,
  "upstreamErrors": 1,
  "bufferedEvents": 120,
  "droppedEvents": 0
}
```

### `GET /api/command-center/traffic?limit=100&decision=all&siteId=&ip=`

สิทธิ์ขั้นต่ำ `SECURITY_ANALYST`; จำกัด `limit` ให้อยู่ระหว่าง 1 ถึง 500 และ default 100

Response เป็น array ของ traffic records โดยทุก field ต้องเป็น sanitized value ตาม data flow ด้านบน ไม่มี body/header/token/raw query

การ filter เป็น server-side สำหรับ limit, decision, siteId และ IP เพื่อไม่ส่งข้อมูลเกินจำเป็นไปยัง Desktop

เมื่อไม่มี event ให้ตอบ `200 []`; เมื่อ storage unavailable ไม่ควรทำให้ Gateway traffic หยุด แต่ API ตอบ `503` พร้อมข้อความที่ไม่เปิดเผย connection string

## Security requirements

- ใช้ session cookie และ role guard เดิม; ห้ามเปิด endpoint นี้แบบ anonymous
- `VIEWER` ไม่มีสิทธิ์อ่าน detailed traffic; `SECURITY_ANALYST` อ่านได้; `OPERATOR` และ `ADMIN` ได้สิทธิ์ตาม level เดิม
- ไม่เชื่อค่า `X-Forwarded-For` ที่ไม่ได้มาจาก trusted proxy configuration
- redact path/query ที่อาจมี secret; ไม่ส่ง Cookie, Authorization, body หรือ raw headers
- จำกัดข้อมูลสูงสุดต่อ response ด้วย server-side limit และ cap ที่ 500
- ใช้ `Cache-Control: no-store` กับ endpoint ทั้งสอง
- เพิ่ม audit event เมื่อ command-center endpoint ถูกปฏิเสธตาม pattern ของ `AdminGuard`
- เพิ่ม structured log เฉพาะ counters/trace ID ที่ไม่เป็นความลับ

## Client contracts

เพิ่ม DTO ใน `AegisWAF.Client.Models` และ methods ใน `ApiClient`:

- `GetCommandCenterSummaryAsync`
- `GetCommandCenterTrafficAsync(CommandCenterTrafficQuery, CancellationToken)`

DTO ต้อง deserialize ได้ทั้ง response ปกติและ empty list โดยไม่ทำให้ UI ล่มเมื่อ field ที่เป็น optional เช่น SiteId หรือ UpstreamId ไม่มีค่า

## Testing strategy

เขียน test ก่อน production implementation ตาม TDD:

- telemetry record redacts query/body-sensitive values
- ring buffer respects configured capacity and reports dropped events
- active connection count returns to zero after success, exception and cancellation
- protected endpoint returns 403 for VIEWER and 200 for SECURITY_ANALYST
- endpoint caps `limit` at 500 and never returns raw query/body/header
- traffic record includes site/upstream/status/decision for allowed request
- blocked request includes rule and OWASP mapping
- API client deserializes summary, traffic and empty results
- UI contract verifies navigation entry, command center view and required columns
- existing security, auth and proxy tests remain green

## Acceptance criteria

ถือว่างานผ่านเมื่อ:

1. เมนูใหม่ปรากฏใน `AegisWAF.exe` และโหลดข้อมูลจริงจาก Gateway API
2. ส่ง request ผ่าน site แล้วเห็น client IP, site, upstream, status และ latency ในหน้า command center
3. ส่ง probe ที่ถูกบล็อกแล้วเห็น decision `BLOCKED`, rule และ OWASP badge โดยไม่มี payload ดิบแสดงใน UI/API
4. ผู้ใช้ role VIEWER อ่าน detailed traffic ไม่ได้
5. buffer ไม่โตไม่จำกัดและ request path ไม่ขึ้นกับความสำเร็จของ telemetry storage
6. tests ที่เพิ่มใหม่และ regression suite ที่เกี่ยวข้องผ่านทั้งหมด
7. build/publish executable สำเร็จและตรวจสอบ binary ที่ output จริง

## Non-goals and future extensions

ระยะถัดไปสามารถเพิ่ม SSE, PostgreSQL/ClickHouse retention, export ไป SIEM, per-site dashboards และ trace drill-down ได้ แต่ต้องมี retention/privacy policy แยกก่อนเปิดใช้งาน
