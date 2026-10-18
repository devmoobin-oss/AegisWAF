# Multi-Site Gateway Design Specification

วันที่: 2026-10-18  
สถานะ: Design approved; awaiting specification review  
เป้าหมาย: ยกระดับ Gateway จาก single-site/single-upstream เป็น Multi-Site Production WAF โดยแยก routing และ security policy ต่อ Site อย่างชัดเจน

## 1. ปัญหาและเป้าหมาย

ปัจจุบัน Gateway มี route และ cluster หลักเพียงชุดเดียว (`route1`, `appcluster`, `app1`) และ Embedded Gateway รับ upstream override ได้เพียงค่าเดียว ทำให้การติดตั้งหนึ่งชุดรองรับการป้องกันแบบ 1:1 เป็นหลัก

การเปลี่ยนแปลงนี้ต้องทำให้ Gateway รองรับหลาย Site บน process เดียว โดย Site แต่ละรายการมี hostname/path matching, upstream, WAF policy, rate limit, egress allowlist และสถานะ health แยกกัน การส่งคำขอข้าม Site ต้องถูกปฏิเสธหรือไม่สามารถเลือก upstream ของ Site อื่นได้

ความสำเร็จวัดจาก:

- Host A ถูกส่งไป upstream A และ Host B ถูกส่งไป upstream B อย่างถูกต้อง
- Site หนึ่งไม่สามารถเข้าถึง upstream หรือ policy ของอีก Site
- Unknown Host ถูกปฏิเสธแบบ fail-closed ใน Production
- Rate limit, egress, event log และ health แยกตาม Site
- Configuration เดิมแบบ single-site ยังทำงานได้ผ่าน `default-site`
- Desktop และ Embedded Gateway สามารถจัดการหลาย Site ได้

## 2. ขอบเขต

### อยู่ในขอบเขต

- Site registry และ schema configuration
- Hostname และ optional path-prefix routing
- Site-scoped upstream selection
- Site-scoped WAF, rate limit, egress และ event context
- Site health/readiness
- Embedded Gateway หลาย Site
- Desktop Site Manager ขั้นพื้นฐาน
- Migration จาก config แบบเดิม
- Unit/integration/security tests

### อยู่นอกขอบเขต

- MFA (ตามข้อกำหนดก่อนหน้า)
- การจัดการ DNS หรือใบรับรอง TLS อัตโนมัติ
- Global CDN/Anycast
- การทำ multi-process orchestration
- การอัปโหลด binary ไป GitHub

## 3. โมเดล Site

เพิ่มโมเดลเชิงตรรกะ `GatewaySite` โดยมีข้อมูลหลัก:

```text
Id: stable identifier, lowercase kebab-case
Enabled: boolean
Hostnames: explicit exact hostnames; optional wildcard policy must be explicit
PathPrefixes: optional normalized prefixes; empty means all paths
Upstreams: one or more HTTP(S) destinations
WafPolicy: named or inline policy reference
RateLimitPolicy: per-site request/global limits
EgressPolicy: per-site allowed hosts/ports/schemes
Health: runtime state, last check and failure reason
```

ข้อกำหนดด้านความปลอดภัย:

- `Id` และ hostname ต้อง normalize ก่อนใช้ match
- hostname matching เป็น exact match โดย default
- wildcard hostname ไม่อนุญาตโดย default และถ้ารองรับต้องตรวจ suffix boundary ไม่ให้ `evil-example.com` match `*.example.com`
- Site ที่ disabled ห้ามรับ traffic
- ห้ามใช้ user-provided Host เป็น upstream address
- Upstream ทุกตัวต้องผ่าน HTTPS/egress validation ใน Production
- ห้ามมี hostname ซ้ำกันระหว่าง Site ที่ active

## 4. Configuration contract

เพิ่ม section `Gateway:Sites` หรือ `Sites` เป็น canonical configuration โดยให้ `Sites` เป็นชื่อหลักเพื่อให้ API/UI ใช้ร่วมกันได้:

```json
{
  "Sites": {
    "site-a": {
      "Enabled": true,
      "Hostnames": ["app-a.example.com"],
      "PathPrefixes": [],
      "Upstreams": [
        { "Id": "primary", "Address": "https://origin-a.example.com/", "Weight": 100 }
      ],
      "WafPolicy": "strict",
      "RateLimit": { "Requests": 300, "WindowSeconds": 60, "GlobalRequests": 10000 },
      "Egress": { "AllowedHosts": ["origin-a.example.com"], "AllowedPorts": [443], "AllowedSchemes": ["https"] }
    }
  }
}
```

ค่าเดิม `ReverseProxy:Routes`, `ReverseProxy:Clusters` และ `ReverseProxy:...Address` จะถูกแปลงเป็น `default-site` เมื่อไม่มี `Sites` section เพื่อรักษา backward compatibility โดยหากมี `Sites` แล้ว จะไม่ใช้ legacy config ปะปนโดย implicit

Validation ขั้น startup/readiness:

- ต้องมี Site active อย่างน้อยหนึ่งรายการ
- ต้องมี hostname อย่างน้อยหนึ่งรายการต่อ Site (ยกเว้น explicit local default mode)
- ต้องมี upstream อย่างน้อยหนึ่งรายการ
- ห้าม hostname/path overlap ที่ทำให้เลือก Site ไม่แน่นอน
- Production ต้องใช้ HTTPS upstream, explicit egress allowlist และ shared rate-limit store ตาม readiness policy เดิม

## 5. Routing และ request flow

ลำดับการทำงานสำหรับ request ที่เป็น proxy traffic:

1. อ่าน Host จาก `Request.Host` และ normalize เป็น lowercase hostname พร้อม port normalization
2. เลือก Site จาก exact hostname และ longest matching path prefix
3. ถ้าไม่พบ Site หรือ Site disabled ให้ตอบ `421 Misdirected Request` หรือ `404` ตาม policy โดย Production default เป็น fail-closed และไม่เปิดเผยรายการ Site
4. ผูก `SiteContext` เข้ากับ `HttpContext`
5. ใช้ WAF policy ของ Site ตรวจ request/body/response
6. ใช้ rate limit key ที่มี SiteId รวมอยู่ด้วย เช่น `site:{siteId}:route:{path}:ip:{ip}`
7. เลือก upstream จากรายการของ Site ที่ผ่าน health/egress validation
8. ส่ง proxy request พร้อม correlation/event context ที่มี SiteId

Admin/API/static routes ต้องถูกแยกจาก proxy routes อย่างชัดเจนและไม่สามารถถูก Site hostname mapping ดักไป upstream ได้

## 6. Policy isolation

เพิ่ม `SiteContext` และ policy resolution layer แทนการอ่าน configuration รวมโดยตรงใน middleware ที่เกี่ยวข้อง:

- `SitePolicyResolver` คืน policy ที่ immutable สำหรับ request หนึ่งรายการ
- WAF rule state เริ่มต้นใช้ global catalog ได้ แต่การเปิด/ปิดและ custom rules ต้องรองรับ Site scope
- Rate limit memory key ต้องมี SiteId; PostgreSQL bucket ต้องมี SiteId เป็นส่วนหนึ่งของ primary uniqueness
- Egress validation ต้องตรวจปลายทางของ Site นั้นเท่านั้น
- Security event ทุกตัวต้องเพิ่ม `siteId`, `hostname` และ `upstreamId` เมื่อทราบค่า
- Audit/API response ต้องไม่รั่วข้อมูลของ Site อื่นให้ผู้ดูแลที่ไม่มี scope

การยกระดับ custom rule จาก global เป็น site-scoped เป็นส่วนหนึ่งของ implementation แต่ไม่จำเป็นต้องเปลี่ยน storage schema ทั้งหมดหากสามารถใช้ nullable `SiteId` และ default global scope ได้อย่างชัดเจน

## 7. Upstream และ health

แต่ละ Site รองรับ upstream อย่างน้อยหนึ่งรายการ และเตรียม abstraction สำหรับหลายรายการ:

- deterministic selection เมื่อมี upstream เดียว
- weighted selection เมื่อมีหลายรายการ
- health state แยกตาม Site/upstream
- unhealthy upstream ถูกตัดออกจาก selection ชั่วคราว
- เมื่อไม่มี upstream ที่ healthy ให้ตอบ `503` พร้อม event ที่ไม่เปิดเผยรายละเอียดภายใน
- destination URL ต้องไม่มี credentials
- DNS/private/metadata checks ใช้ `UpstreamEgressPolicy` ของ Site

Health probe ต้องมี timeout, bounded response และไม่ส่ง authentication secret ไปยังปลายทาง

## 8. API และ Desktop UX

เพิ่ม API สำหรับ Site management ภายใต้ admin authorization เดิม:

- `GET /api/sites`
- `GET /api/sites/{id}`
- `POST /api/sites`
- `PUT /api/sites/{id}`
- `DELETE /api/sites/{id}` หรือ disable แบบ soft-delete
- `POST /api/sites/{id}/health-check`

API ต้อง validate overlap, hostname, upstream scheme และ egress policy ก่อนบันทึก

Desktop เพิ่ม `SitesView`/Site Manager โดยแสดง:

- ชื่อ Site, hostname, จำนวน upstream, health และ policy
- สถานะป้องกัน OWASP พร้อม icon
- ปุ่มเพิ่ม/แก้ไข/disable และตรวจสอบ upstream
- ตัวชี้วัด blocked/allowed requests ต่อ Site
- ข้อความเตือนเมื่อ Site ใช้ค่าไม่พร้อม Production

การแก้ไข Site ต้องไม่ทำให้ process ต้อง restart หากทำได้อย่างปลอดภัย; หาก runtime reload ยังไม่ปลอดภัย ให้บันทึกเป็น pending และแสดงสถานะชัดเจน

## 9. Error handling และ observability

กำหนด reason code ที่คงที่:

- `site_not_found`
- `site_disabled`
- `site_hostname_conflict`
- `site_no_healthy_upstream`
- `site_egress_denied`
- `site_policy_invalid`

ทุก error ที่ส่งให้ client ต้องเป็นข้อความทั่วไป ส่วนรายละเอียดอยู่ใน structured log เท่านั้น Log fields ขั้นต่ำคือ `siteId`, `host`, `path`, `requestId`, `decision`, `reason`, `upstreamId` และ latency

Metrics ขั้นต่ำ:

- request count / blocked count ต่อ Site
- rate-limit denied ต่อ Site
- upstream health และ latency ต่อ Site
- parser/body inspection rejection ต่อ Site

## 10. Testing strategy

### Unit tests

- hostname normalization และ exact matching
- wildcard boundary ถ้ารองรับ
- longest path-prefix matching
- duplicate hostname/overlap validation
- legacy config migration เป็น default-site
- Site-scoped rate-limit keys
- Site-scoped egress validation
- upstream selection และ unhealthy fallback

### Integration tests

- Host A -> upstream A
- Host B -> upstream B
- Host A cannot reach B
- unknown host fail-closed
- path prefix chooses the more specific Site
- body/response inspection uses selected Site policy
- events include SiteId
- Desktop Embedded Gateway starts with multiple Sites

### Production/security tests

- Host header ambiguity and port variants
- IDN/punycode normalization policy
- wildcard bypass attempts
- SSRF against private, loopback, link-local and metadata targets per Site
- rate-limit isolation and global limits
- config reload race and partial invalid config
- no cross-site data leakage in API/log responses

## 11. Rollout and compatibility

เฟสแรกเพิ่ม model/resolver และรองรับ config โดยยังคง legacy fallback 1:1  
เฟสสองเปลี่ยน middleware/rate-limit/egress/event ให้ใช้ SiteContext  
เฟสสามเพิ่ม API/Desktop Site Manager และ health UI  
เฟสสี่เปิด production readiness checks และ migration guidance

ค่า default ของ Desktop จะสร้าง `default-site` จาก upstream เดิม เพื่อให้ผู้ใช้เดิมเปิดโปรแกรมได้เหมือนเดิม แต่โหมด Production จะบังคับ explicit hostname และ egress allowlist

## 12. Non-goals และข้อจำกัดที่ต้องประกาศ

Multi-Site ไม่ได้หมายความว่าเป็น distributed control plane หรือ certificate manager การ deploy หลาย instance ยังต้องใช้ shared PostgreSQL สำหรับ rate limit/auth และต้องทำ external load/security testing ก่อนประกาศ Production readiness เต็มรูปแบบ

