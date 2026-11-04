# Multi-Site persistence upstream boundary

## เป้าหมาย

ทำให้ข้อมูล Upstream ที่อ่านจาก PostgreSQL ผ่าน invariant เดียวกับ API, configuration snapshot และ client โดยไม่ยอม materialize URL ที่มี query string หรือ fragment

## ขอบเขต

- `SitePersistenceCodec.FromRow` ต้องปฏิเสธ `?query` และ `#fragment` ก่อนสร้าง `SiteUpstream`
- ยังคงอนุญาต HTTP(S) base path เช่น `https://origin.example.com/app/`
- ไม่เปลี่ยนการตรวจ credentials, weight, egress, health หรือ route activation ที่มีอยู่
- ป้องกันข้อมูลเก่าหรือข้อมูลที่ถูกแก้ตรงในฐานข้อมูลไม่ให้ข้าม persistence boundary

## เกณฑ์สำเร็จ

- persistence regression แสดง RED ก่อนแก้และ GREEN หลังแก้
- targeted Multi-Site regression และ Release build ผ่านโดยไม่มี warning/error
- publish artifact และ manifest ใหม่ตรวจสอบผ่าน
- บันทึก before/after เป็น 0% → 100% ใน `handoff.md`

## ขอบเขตที่ยังไม่อ้างสิทธิ์

ยังไม่ครอบคลุมการทดสอบ PostgreSQL จริง, Browser/EXE end-to-end, external penetration/load/SLO/SIEM/CodeQL หรือ Authenticode signing
