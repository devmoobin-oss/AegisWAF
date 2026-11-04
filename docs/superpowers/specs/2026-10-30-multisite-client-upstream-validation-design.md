# Multi-Site client upstream validation parity

## เป้าหมาย

ทำให้ Browser และ Desktop ตรวจสอบ URL ของ Upstream ด้วยกติกาเดียวกับ Gateway ก่อนส่งคำขอ เพื่อให้ผู้ดูแลเห็นข้อผิดพลาดทันทีและไม่เกิดความแตกต่างระหว่างช่องทางการจัดการ Site

## ขอบเขต

- Browser parser ต้องปฏิเสธ `?query` และ `#fragment` ในทุกบรรทัดของ Upstream
- Desktop ฟอร์มหลักและ Advanced Upstreams ต้องใช้ validation ที่ปฏิเสธ query/fragment ก่อนสร้าง request
- ใช้ข้อความภาษาไทยที่อธิบายให้ย้ายข้อมูลไปเป็น base path
- คงการอนุญาต URL HTTP(S) ที่ไม่มี credentials และมี base path ได้
- ไม่เปลี่ยนกติกา health, rate limit, egress หรือ activation นอกเหนือจาก boundary ของ URL

## เกณฑ์สำเร็จ

- RED tests แสดงความล้มเหลวของ Browser และ Desktop boundary ก่อนแก้
- focused tests ของ validator และ source contracts ผ่าน 100%
- targeted Multi-Site regression, Release build, Node syntax check และ diff check ผ่าน
- publish artifact ใหม่และ manifest ตรวจสอบผ่าน
- บันทึก before/after เป็น 0% → 100% ใน `handoff.md`

## ขอบเขตที่ยังไม่อ้างสิทธิ์

ยังไม่ครอบคลุมการยิง request ผ่าน Browser จริงกับ PostgreSQL จริง หรือการยืนยัน Authenticode certificate ของ EXE
