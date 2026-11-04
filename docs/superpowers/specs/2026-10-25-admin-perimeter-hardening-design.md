# Admin Perimeter Hardening Design

## Intent

เพิ่มชั้นป้องกัน control plane ของ AegisWAF ให้แยกจาก protected-app traffic ได้เมื่อ deploy แบบ production โดยไม่ทำลาย embedded Desktop profile ที่ใช้ listener เดียวและ HTTP loopback ได้

## Goals

- ตรวจ `X-Forwarded-Proto` และ `X-Forwarded-Host` เฉพาะเมื่อ direct peer อยู่ใน `Security:TrustedProxies`
- ให้ remote admin ผ่านได้เฉพาะ effective scheme ที่ตรวจสอบแล้วว่าเป็น HTTPS
- เพิ่ม optional listener-port isolation: admin paths ใช้ `Admin:ListenerPort` และ protected-app paths ใช้ `Gateway:ListenerPort`
- ให้ session cookie ใช้ effective HTTPS policy เดียวกับ admin guard
- เพิ่ม regression tests สำหรับ trusted/untrusted proxy, forwarded host/origin และ port isolation
- อัปเดต production readiness และ module handoff ด้วย evidence ที่ตรวจสอบได้

## Non-goals

- ไม่เพิ่ม MFA/WebAuthn/OIDC ในโมดูลนี้
- ไม่จัดการ certificate provisioning หรือ firewall ของ operating system
- ไม่บังคับ port isolation ใน Desktop/default mode ที่ไม่มีการกำหนด listener ports
- ไม่ถือว่า local tests เป็นการรับรอง external penetration test

## Approaches considered

1. เปลี่ยนเฉพาะ `AdminGuard` ให้ดู forwarded headers: แก้เร็ว แต่ยังปล่อย admin/proxy routes บน listener เดียวกัน
2. เพิ่ม policy กลางสำหรับ effective request metadata และ optional dual-listener isolation: เปลี่ยนหลายจุดเล็กน้อย แต่มี boundary เดียวให้ guard, cookie และ middleware ใช้ร่วมกัน — **เลือกแนวทางนี้**
3. แยกเป็นสอง ASP.NET host/process ทันที: isolation แข็งแรงที่สุด แต่กระทบ EmbeddedGateway, lifecycle และ deployment มากเกินขอบเขตโมดูลนี้

## Configuration contract

```json
{
  "Admin": {
    "AllowRemote": false,
    "RequireHttps": true,
    "ListenerPort": 0
  },
  "Gateway": {
    "ListenerPort": 0
  },
  "Security": { "TrustedProxies": [] }
}
```

ค่า `0` หมายถึงไม่บังคับ port isolation เพื่อรักษา compatibility เดิม ส่วน production deployment ที่ต้องการ separation ต้องกำหนดสอง port ให้ต่างกันและให้ infrastructure bind listener ทั้งสอง port

## Security invariants

- Untrusted peers cannot influence client IP, effective scheme, or effective host through forwarded headers.
- Remote admin is denied unless the effective scheme is HTTPS.
- When both listener ports are configured, admin-owned paths cannot be reached through the gateway listener and proxy paths cannot be reached through the admin listener.
- HTTP loopback Desktop remains usable when remote admin is disabled and listener ports are unset.
