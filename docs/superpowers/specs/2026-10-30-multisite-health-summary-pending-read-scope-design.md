# Multi-Site health summary pending read scope

## Intent

ทำให้ Summary อ่านข้อมูลจาก PostgreSQL และ process-memory เฉพาะ Site ที่ route active อยู่จริง เมื่อ Site ที่บันทึกไว้ยังรอ restart (`pending-restart`) จะไม่ทำ query ประวัติหรือ acknowledgement ของ Site นั้นโดยไม่จำเป็น

## Goals

- คำนวณ `activeSiteIds` จาก Site ที่ไม่อยู่ใน `pendingSiteIds`
- ใช้ `activeSiteIds` เป็นขอบเขตของ health history และ alert acknowledgement reads
- ไม่เรียก database read สองชุดเมื่อทุก Site ในหน้าปัจจุบัน pending
- คง pending Site ไว้ใน response โดยคืนข้อมูล trend เป็น `pending-restart`
- ไม่นับ process-memory history ของ pending Site เป็น `historyRetention` ของ active runtime

## Non-goals

- ไม่ activate, mutate, delete หรือเปลี่ยน Site configuration
- ไม่ลบข้อมูลย้อนหลังใน PostgreSQL
- ไม่เปลี่ยนการ cleanup acknowledgement ที่หมดอายุ
- ไม่เปลี่ยน role, CSRF, timeout budget หรือการ fallback ของ active Site

## Verification boundary

ใช้ contract tests สำหรับขอบเขต history/acknowledgement reads, targeted Multi-Site regression, Release build, JavaScript syntax check, diff check และ publish manifest verification
