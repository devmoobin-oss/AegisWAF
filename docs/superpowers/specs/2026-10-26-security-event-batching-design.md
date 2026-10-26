# Security event batching and backpressure — design

## Goal

ลด overhead และ connection churn ของ `EventWriter` โดยเก็บ security events ใน bounded queue แล้ว persist เป็น batch ใน transaction เดียว พร้อมทำให้ queue pressure/persistence failure มองเห็นได้จาก operator API

## Invariants

- Event queue ต้อง bounded และ `TryWrite` ต้องไม่ block request pipeline
- เมื่อ queue เต็ม ต้องนับ dropped event และ emit `WAF_SECURITY_EVENT_DROPPED`
- Batch ต้องอ่านตามลำดับ, จำกัดจำนวน และมี flush timeout เพื่อไม่ค้าง event เดี่ยว
- Database batch ต้องใช้ transaction เดียว; failure ต้องถูกนับและ log โดยไม่ทำให้ Gateway หยุด
- Production config ต้องมี bounds ที่ชัดเจน ห้าม clamp ค่าที่ไม่ปลอดภัยแบบเงียบ ๆ
- JSON structured logging เดิมยังคงทำงานต่อ event

## Non-goals

ยังไม่เพิ่ม durable retry queue หรือ SIEM transport ภายนอก เพราะต้องกำหนด retention/delivery contract และทดสอบ PostgreSQL/SIEM จริงแยกต่างหาก
