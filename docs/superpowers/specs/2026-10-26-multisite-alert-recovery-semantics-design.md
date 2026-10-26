# Multi-site Alert Recovery Semantics Design

## Intent

ทำให้ผู้ดูแลแยกได้ชัดระหว่างสถานะปัจจุบันของ Site กับประวัติการล้มเหลว โดยเมื่อ observation ล่าสุดกลับมา `healthy` หลังมี `unhealthy/unknown` ใน window เดียวกัน ระบบจะแสดงสถานะการฟื้นตัวว่า `recovered` แทนการทำให้ประวัติหายไปหรือแสดงวิกฤตโดยไม่อธิบาย

## Goals

- รักษา `alertState` เดิมสำหรับการวิเคราะห์แนวโน้มและ audit history
- เพิ่ม `recoveryState` ที่มีค่า `none` หรือ `recovered`
- เมื่อ observation ล่าสุด healthy และมี failure เก่าใน window ให้ UI แสดง “กลับมาปกติ · มีประวัติเก่า” สีเขียว
- ไม่ให้ Site ที่ recovered ถูกนับเป็น actionable สำหรับการกดรับทราบซ้ำ
- เพิ่มจำนวน `recoveredSites` ใน summary เพื่อให้ตัวเลขด้านบนตรงกับแถว Site
- รักษา counts, percentage, consecutive-unhealthy และ history เดิมไว้ทั้งหมด

## Non-goals

- ไม่ลบหรือแก้ไข observation เก่า
- ไม่เปลี่ยน threshold `critical` หรือ `degraded`
- ไม่ทำ auto-acknowledge และไม่ลบ acknowledgement ในฐานข้อมูล
- ไม่เพิ่มการ polling หรือ API call ใหม่

## Approaches

1. เปลี่ยน `alertState` ให้เป็น `recovered` — สั้นแต่ทำให้ historical alert semantics หายและกระทบ acknowledgement/query เดิม
2. เพิ่ม `recoveryState` ควบคู่กับ `alertState` — เลือกแนวทางนี้ เพราะรักษา compatibility และสื่อสถานะปัจจุบันได้ครบ
3. ลบ history เมื่อ probe healthy — ไม่เลือก เพราะทำลายหลักฐานและทำให้การวิเคราะห์ trend สูญเสียข้อมูล

## Design

- `SiteHealthTrend` และ client DTO เพิ่ม `RecoveryState` แบบมี default `none` เพื่อรักษา compatibility กับ payload เก่า
- `SiteHealthTrendCalculator` กำหนด `recovered` เมื่อ observation ล่าสุด healthy และ observation อื่นใน window มี unhealthy หรือ unknown
- API trend/summary ส่ง `recoveryState` เพิ่มโดยไม่เปลี่ยน `alertState`
- Summary เพิ่ม `recoveredSites`; `degradedSites` จะไม่นับ Site ที่ recovered เพื่อให้ภาพรวม operator สอดคล้องกับแถว
- Desktop row ใช้ `recoveryState` เป็นสถานะแสดงผลและปิด acknowledge เมื่อ recovered; historical percentage/count ยังแสดงเหมือนเดิม

## Success criteria

- มี test ยืนยัน failure history ตามด้วย healthy ล่าสุดให้ `alertState=degraded`, `recoveryState=recovered`
- healthy ต่อเนื่องและ no-data ให้ `recoveryState=none`
- UI contract ตรวจคำว่า recovered และ DTO/API contract
- focused trend/alert/UI regression ผ่าน 100%
- solution build 0 warning/0 error และบันทึก Before/After ใน `handoff.md`
