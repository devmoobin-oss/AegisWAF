# Multi-site Recovery Timeline Export Design

## Goal

ให้ผู้ปฏิบัติงานคัดลอก timeline ของ recovery จากหน้า Multi-site เพื่อส่งต่อให้ทีม support ได้ โดยข้อมูลต้องเป็น local-only, bounded และ redacted ตั้งแต่ชั้น domain ก่อนถึง clipboard

## Scope

- รับเฉพาะ cancellation events และ safe recovery events ที่มีอยู่ใน client state
- แสดง cancellation reason จาก enum allowlist เท่านั้น
- แสดง recovery kind จาก allowlist (`failure`, `recovered`) เท่านั้น
- เรียง cancellation ด้วย monotonic sequence ไม่ใช้ timestamp เป็นตัวตัดสิน
- จำกัดรายการที่ export ต่อประเภทไม่เกิน 3 รายการ และจำกัด output ไม่เกิน 1024 ตัวอักษร
- ไม่ export Site ID, hostname, upstream, exception, database, SQL, token หรือ raw event kind
- UI คัดลอกข้อความผ่าน clipboard ในเครื่องเท่านั้น และต้อง disable เมื่อไม่มี timeline

## Acceptance criteria

1. Export ที่มีข้อมูลต้องมี header และ safe labels ที่ตรวจสอบได้
2. เหตุผลที่เลือก filter ต้องถูกใช้กับ cancellation events โดย unknown reason ไม่ถูกพิมพ์ออก
3. Unknown recovery kind ต้องถูกแทนด้วยข้อความ redacted ไม่ใช่ raw value
4. Output ต้องมีความยาวไม่เกิน 1024 ตัวอักษรแม้ input จะยาวหรือมีจำนวนมาก
5. UI มีปุ่มคัดลอกที่เข้าถึงได้จาก Recovery Expander และไม่เรียก API เพิ่ม
6. Existing recovery failure count และ diagnostics behavior ต้องไม่เปลี่ยน
