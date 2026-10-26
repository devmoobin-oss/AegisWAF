# Multi-site Hardening Design

## Intent

ยกระดับ Multi-site จากฟังก์ชันที่ทำงานได้ในระดับ application ให้มีความถูกต้อง ปลอดภัย และตรวจสอบย้อนหลังได้ในระดับ production โดยใช้กฎ validation ชุดเดียวกันตั้งแต่ configuration, API mutation, database snapshot และ runtime routing

## Goals

- ทุก Site ที่เข้าระบบต้องผ่าน typed validation ชุดเดียวกัน
- ป้องกัน unsafe upstream, undefined WAF policy, duplicate hostname และ malformed persistence data
- เก็บ health/rate-limit policy ครบและ round-trip ได้หลัง restart
- บันทึก audit event ของ create/update/delete ใน transaction เดียวกับ mutation
- ใช้ startup snapshot ชุดเดียวกับทุก runtime consumer
- มี regression/integration tests และหลักฐานใน `handoff.md`

## Non-goals

- ไม่เปลี่ยน WAF inspection rules ที่ไม่เกี่ยวกับ Site lifecycle
- ไม่เพิ่ม external SIEM, MFA/OIDC หรือ penetration test ในรอบนี้
- ไม่เปลี่ยนรูปแบบ UI ทั้งหมด หาก backend contract ยังไม่พร้อม

## Constraints

- ใช้ .NET และ PostgreSQL ตามโครงสร้างเดิม
- ต้องรักษา compatibility กับ legacy configuration และ migration เดิม
- ห้ามเก็บ credentials ใน upstream URI
- Production mode ต้องเป็น HTTPS-only และมี explicit egress allowlist
- ทุกโมดูลต้องมีหลักฐานก่อน/หลังใน `handoff.md`

## Approaches

1. แก้เฉพาะ API validation: ไม่พอ เพราะ DB snapshot สามารถข้าม validator ได้
2. แก้ database schema ก่อน: ไม่พอ เพราะ API/runtime ยังใช้กฎคนละชุด
3. รวม validation และ lifecycle เป็น pipeline เดียว: เลือกแนวทางนี้ เพื่อให้ config, API, DB และ runtime มี invariant เดียวกัน

## Module boundaries

1. Snapshot validation parity and secure mutation
2. Persistence completeness and corrupt-row handling
3. Hostname uniqueness and atomic audit trail
4. Single immutable startup snapshot and operational errors
5. Health probe semantics, readiness and observability
6. Multi-site UI completeness and API contract
7. Integration, concurrency, restart and production verification

## Success criteria

แต่ละโมดูลจะได้ 100% เมื่อ implementation, focused tests, solution build และ module-specific evidence ผ่านครบ ไม่มี failed หรือ unverified gate และมีบันทึก before/after ใน `handoff.md`
