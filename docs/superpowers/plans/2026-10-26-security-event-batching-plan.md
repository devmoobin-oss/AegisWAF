# Security event batching and backpressure — implementation plan

## Task 1 — RED tests

- ทดสอบ bounded queue, dropped count, queue depth และ batch ordering
- ทดสอบ flush timeout เมื่อ event น้อยกว่าขนาด batch
- ทดสอบ Production readiness rejects unsafe queue/batch/flush values

## Task 2 — Implement bounded batching

- เพิ่ม queue capacity, batch size และ flush window configuration
- เพิ่ม `ReadBatchAsync` พร้อม bounded wait
- persist หลาย event ผ่าน connection/transaction/command เดียว
- เพิ่ม persisted/failure counters ใน overview

## Task 3 — Verification

- รัน focused in-process tests และ solution build
- เมื่อ PostgreSQL พร้อม ให้รัน integration ที่ตรวจ row count, transaction commit และ failure behavior
- บันทึกคะแนนที่ local gate ทำได้และ external DB gate ที่ยังค้างใน `handoff.md`
