# Multi-Site upstream URL boundary

## เป้าหมาย

ป้องกันไม่ให้ Site upstream เก็บ query string หรือ fragment ติดมากับปลายทางของ reverse proxy โดยไม่ตั้งใจ ซึ่งทำให้ route และ health-check มีความหมายคลาดเคลื่อนจาก host/port/base path ที่ผู้ดูแลเห็นในหน้า Multi-Site

## ขอบเขต

- ชั้น API editor ต้องปฏิเสธ upstream ที่มี `?query` หรือ `#fragment` พร้อมข้อความแก้ไขได้
- ชั้น `Validate` และ `ValidateSnapshot` ต้องปฏิเสธซ้ำ เพื่อป้องกันข้อมูลจากไฟล์หรือ PostgreSQL ที่ถูกแก้ตรง
- อนุญาต base path เช่น `https://origin.example.com/app/`
- ไม่เปลี่ยนการตรวจ scheme, credentials, egress, health หรือการ activate route นอกเหนือจาก boundary นี้

## เกณฑ์สำเร็จ

- กรณี API และ configuration snapshot มี regression test ครบ
- focused configuration regression ผ่านทั้งหมด
- Release build และ `git diff --check` ผ่าน
- บันทึก before/after เป็น 0% → 100% ใน `handoff.md`

## ขอบเขตที่ยังไม่อ้างสิทธิ์

ยังไม่ครอบคลุมการทดสอบกับ PostgreSQL จริง, published EXE แบบ authenticated, การยิง request ผ่าน upstream จริง หรือการตรวจ certificate/penetration ภายนอก
