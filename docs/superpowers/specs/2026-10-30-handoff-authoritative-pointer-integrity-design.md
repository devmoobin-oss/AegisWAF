# Handoff authoritative pointer integrity

## Intent

ทำให้ `handoff.md` เป็นแหล่งสถานะล่าสุดที่ AI รอบถัดไปอ่านได้อย่างถูกต้อง โดยคงประวัติย้อนหลังทั้งหมดไว้ แต่บังคับให้ current pointer อยู่ท้ายไฟล์จริงเสมอ

## Goals

- current pointer ของโมดูลล่าสุดต้องเป็น marker ล่าสุดในไฟล์
- หลัง marker ล่าสุดต้องไม่มี historical pointer หรือข้อความอื่นต่อท้าย ยกเว้น whitespace
- pointer ต้องมี progress และ artifact ของโมดูลล่าสุด

## Non-goals

- ไม่ลบหรือเรียงประวัติเดิม
- ไม่แก้ไข runtime behavior ของ Gateway/Desktop
- ไม่เปลี่ยนสถานะ Module 41 และ Module 60 ที่ยังติด external CodeQL gate

## Approach

เก็บ historical records เดิมไว้ แล้วเพิ่ม final pointer ที่ EOF พร้อมเพิ่ม contract test ที่ตรวจ `handoff.TrimEnd().EndsWith(pointer)` และตรวจ marker ล่าสุดโดยตรง วิธีนี้แก้ความคลุมเครือโดยไม่ทำลาย audit trail

## Verification boundary

ทดสอบด้วย in-process contract runner, ตรวจ whitespace ด้วย `git diff --check`, และตรวจว่า `handoff.md` มี pointer หลัง Module 178 อยู่ท้ายไฟล์จริง
