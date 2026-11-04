# Module 216 — File Site ID Uniqueness Parity

## Intent

ทำให้การตรวจ Site ID จากไฟล์ configuration ปฏิเสธ ID ที่ซ้ำกันหลัง trim/lowercase เช่นเดียวกับ typed database snapshot และ runtime registry

## Evidence and root cause

`ValidateSnapshot` มี `siteIds` และตรวจ duplicate หลัง normalization แต่ `Validate(IConfiguration)` ตรวจเพียงรูปแบบ ID จึงคืนรายการ error ว่างสำหรับ `Sites: Shop ` และ `Sites:shop` แม้ `SiteConfigurationLoader` จะ bind เป็น ID เดียวกัน

## Scope

- เพิ่ม normalized Site ID set ใน `Validate(IConfiguration)`
- ตรวจทั้ง Site ที่ enabled และ disabled ให้สอดคล้องกับ typed snapshot
- ไม่เปลี่ยนการ normalize, routing หรือ Site ID ที่ valid เพียงตัวเดียว

## Verification

ใช้ test ที่สร้าง in-memory file configuration สอง Site ซึ่งต่างกันด้วย whitespace/case แล้วตรวจว่า validator รายงาน duplicate ก่อนแก้ (RED) และผ่านหลังแก้ (GREEN) จากนั้นรัน full suite, build และ publish
