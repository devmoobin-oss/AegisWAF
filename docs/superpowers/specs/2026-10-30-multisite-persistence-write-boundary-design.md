# Multi-Site persistence write boundary

## เป้าหมาย

ทำให้ persistence codec ปฏิเสธ Upstream ที่ผิด invariant ทั้งตอนเขียนและตอนอ่าน เพื่อไม่ให้โค้ดภายในหรือ seed path บันทึก URL ที่มี query string หรือ fragment ลง PostgreSQL ได้

## ขอบเขต

- `SitePersistenceCodec.ToRow` ต้องปฏิเสธ `?query` และ `#fragment` ก่อนสร้าง row
- คง invariant เดียวกับ `FromRow`, API editor, configuration validator และ client
- อนุญาต HTTP(S) base path ที่ไม่มี query/fragment
- ไม่เปลี่ยน schema หรือเส้นทาง activation

## เกณฑ์สำเร็จ

- RED test ยืนยันว่า `ToRow` เดิมเขียนข้อมูลผิดได้
- GREEN persistence tests ผ่านครบ
- targeted Multi-Site regression, Release build, syntax/diff และ publish manifest ผ่าน
- บันทึก before/after เป็น 0% → 100% ใน `handoff.md`

## ขอบเขตที่ยังไม่อ้างสิทธิ์

ยังไม่ครอบคลุม PostgreSQL จริง, Browser/EXE end-to-end, external security gates หรือ Authenticode signing
