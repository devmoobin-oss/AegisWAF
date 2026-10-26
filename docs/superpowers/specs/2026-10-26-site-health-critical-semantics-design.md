# Site Health Critical Semantics Design

## Intent

ทำให้สถานะ `critical` ของ Multi-Site สะท้อนความล้มเหลวของ Site จริง ไม่ใช่ผลจากการนับ observation ของ upstream หลายตัวที่ถูกเรียงรวมกันจนกลายเป็น streak เทียม

## Current finding

`SiteHealthTrendCalculator` นับ `ConsecutiveUnhealthy` จากรายการ observation ที่เรียงตามเวลาโดยไม่จัดกลุ่มเป็นรอบตรวจและไม่รู้จำนวน upstream ที่ Site คาดหมาย การที่ upstream หลายตัวล้มเหลวในรอบเดียว หรือมี upstream เพียงบางตัวถูกบันทึก อาจทำให้เกิด `critical` แม้ Site ยังมี upstream ที่ใช้งานได้

## Goals

- ใช้ timestamp เดียวกันสำหรับทุก upstream ที่ถูกตรวจใน `SiteHealthProbe.ProbeAsync` หนึ่งรอบ
- คำนวณ consecutive failure เป็นจำนวนรอบตรวจของ Site ไม่ใช่จำนวนแถว observation
- ให้ `critical` เกิดเมื่อ upstream ที่คาดหมายทั้งหมดล้มเหลวต่อเนื่องอย่างน้อย 3 รอบ
- ให้ partial failure เป็น `degraded` แม้เกิดซ้ำหลายรอบ แต่ยังมี upstream ที่ healthy
- รักษาสถิติ observation, recovery state, stale state และ API compatibility เดิม
- ไม่เปิดเผยข้อมูลลับหรือเปลี่ยน policy ของ egress/WAF

## Non-goals

- ไม่ลบประวัติ health เดิม
- ไม่ลด threshold Critical จาก 3 รอบ
- ไม่เปลี่ยนสถานะ live routing ของ YARP
- ไม่แก้ด้วยการซ่อน/เปลี่ยนข้อความ UI เพียงอย่างเดียว

## Approaches

1. UI-only relabeling: ไม่แก้ logic และยังสร้าง false-critical; rejected.
2. ล้าง historical state เมื่อ live กลับมา healthy: ลดการแจ้งเตือนแต่สูญเสีย audit; rejected.
3. Batch-aware site-level trend: ใช้ probe timestamp ต่อรอบ, expected upstream count และ streak ต่อ batch; selected เพราะแก้สาเหตุและรักษาหลักฐาน.

## Semantics

- `expectedUpstreamCount` มาจาก `GatewaySite.Upstreams.Count` ใน production; calculator ยังคง infer จาก distinct upstream IDs เมื่อผู้เรียกเก่าไม่ได้ส่งค่า เพื่อรักษา compatibility ของ unit tests/consumers.
- observation ที่ timestamp เดียวกัน หรือถูก normalize อยู่ในวินาทีเดียวกัน ถือเป็น batch เดียวกัน
- batch เป็น site-failed เมื่อมี upstream ครบตามจำนวนที่คาดหมายและทุก observation ใน batch เป็น `unhealthy`
- `ConsecutiveUnhealthy` คือจำนวน batch ล่าสุดที่เป็น site-failed
- `critical` ต้องมี consecutive failed batches >= 3
- `degraded` ใช้เมื่อมี unhealthy/unknown แต่ยังไม่เข้า critical หรือ batch ไม่ครบ upstream
- `recovered` ต้องมี latest batch healthy ครบ expected upstream และมี historical failure ที่กลับมา healthy

## Safety and compatibility

- ถ้า expected count ไม่ทราบ ใช้ distinct upstream IDs ที่มีในข้อมูลที่เลือกเป็น fallback; ข้อมูลไม่ครบจะไม่ถูกยกระดับเกินจริง
- สถิติ `Total`, `Healthy`, `Unhealthy`, `Unknown`, `HealthyPercent` ยังคงนับ observation เพื่อไม่ทำลาย dashboard contract
- เปลี่ยนเฉพาะความหมายของ streak/alert state และทำให้ field นี้ชัดเจนใน tests/docs

## Acceptance evidence

- RED test แสดงว่า multi-upstream observations ใน batch เดียวไม่ถูกนับเป็น 3 รอบ
- GREEN tests ครอบคลุม all-upstream failure, partial failure, incomplete batch, timestamp normalization, recovery และ single-upstream compatibility
- Gateway build ผ่าน
- focused health-trend tests ผ่าน หรือถ้า test project ถูกขัดขวางด้วย SDK environment ต้องมี runtime evidence และระบุข้อจำกัดตามจริง
- `HANDOFF.md` บันทึก Before/After และ artifact ของ Module 50
