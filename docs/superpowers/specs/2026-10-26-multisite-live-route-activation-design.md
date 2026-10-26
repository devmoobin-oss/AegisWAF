# Multi-site Live Route Activation Design

## Intent

ให้การเพิ่ม แก้ไข และลบ Site จาก Multi-site Desktop มีผลกับ route ที่กำลังรันอยู่ทันที โดยไม่ต้องปิดหรือ restart Gateway และยังรักษา PostgreSQL เป็น source of truth

## Current gap

ปัจจุบัน `SiteRegistry` และ YARP configuration ถูกสร้างครั้งเดียวตอน `GatewayApp.CreateBuilder`; mutation endpoints บันทึก PostgreSQL แล้วตอบ `requiresRestart=true` แต่ไม่ได้เปลี่ยน in-memory registry หรือ proxy routes

## Chosen approach

ใช้ YARP `InMemoryConfigProvider` เป็น dynamic route provider และเพิ่ม `SiteRuntimeCoordinator` ที่ทำงานกับ immutable `SiteConfigurationSnapshot`:

1. โหลด/validate candidate snapshot จาก registry ปัจจุบัน
2. บันทึก PostgreSQL สำเร็จ
3. สร้าง route/cluster snapshot ใหม่และเรียก provider update
4. publish registry snapshot ใหม่แบบ atomic
5. API ตอบ `active=true`, `requiresRestart=false`

ถ้า persistence หรือ route snapshot สร้างไม่ได้ จะไม่ publish registry ใหม่และส่ง error โดย route เดิมยังคงอยู่

## Scope

- add/update/delete Site activate live
- YARP route, cluster, destination, health check, transform และ metadata ต้องเปลี่ยนตาม snapshot ใหม่
- Site matching, WAF policy, health/ready checks และ API list ต้องเห็น snapshot เดียวกัน
- Desktop copy/ข้อความต้องไม่บอกให้ restart หลัง mutation สำเร็จ
- รักษา row-version/ETag, validation, audit และ hostname uniqueness เดิม

## Non-goals

- ไม่เปลี่ยน schema PostgreSQL
- ไม่ทำ multi-node distributed route propagation; การเปลี่ยน live มีผลต่อ process ที่รับ mutation เท่านั้น
- ไม่เพิ่ม background polling หรือ restart process

## Acceptance criteria

1. เพิ่ม Site แล้ว request ที่ใช้ hostname/path ใหม่ถูก route โดยไม่ restart
2. แก้ไข Site แล้ว request ใช้ upstream/policy/path snapshot ใหม่ และ stale ETag ยังถูกปฏิเสธ
3. ลบ Site แล้ว request เดิมได้ 421 และ route/cluster ไม่เหลือ
4. Update แข่งขันกันยังคง CAS/ETag และไม่ publish stale snapshot
5. Route update มี rollback/no-publish เมื่อ candidate invalid หรือ provider update ล้มเหลว
6. Existing full regression, PostgreSQL, API authorization และ Desktop contract tests ยังคงผ่าน
