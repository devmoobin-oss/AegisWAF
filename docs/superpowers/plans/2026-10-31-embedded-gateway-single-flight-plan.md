# Module 215 — Embedded Gateway Single-Flight Startup Plan

- [x] เพิ่ม regression test สำหรับ concurrent `StartAsync` ก่อนแก้ production code
- [x] รัน RED และบันทึก failure ที่เกิดจาก concurrent Bootstrap/hostname seed
- [x] เพิ่ม lifecycle semaphore ใน `EmbeddedGateway` ครอบคลุม start/stop
- [x] รัน GREEN และ Embedded Gateway regression suite
- [x] รัน Multi-Site source/runtime regression, build และ publish
- [x] สร้าง verification artifact และอัปเดต `handoff.md` พร้อม before/after %
