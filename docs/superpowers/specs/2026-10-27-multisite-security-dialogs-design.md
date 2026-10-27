# Module 98 — Security Action Dialogs Design

## Intent

ทำให้การลบ Custom WAF Rule และ IP Policy ใช้ dialog ที่เป็นของแอปและเข้าถึงได้ แทน `window.confirm()`/`alert()` ของเบราว์เซอร์ เพื่อให้ผู้ปฏิบัติงานเข้าใจผลกระทบ ยืนยันการกระทำด้วยคีย์บอร์ดได้ และได้รับข้อความผิดพลาดที่สอดคล้องกับ API ProblemDetails โดยไม่เปลี่ยนสัญญา API หรือสิทธิ์การใช้งาน

## Goals

- มี dialog กลางชื่อ `showAppDialog` สำหรับ destructive confirmation และ error information ที่เกิดซ้ำในคอนโซล
- dialog มี accessible name/description, `role="alertdialog"`, `aria-modal`, initial focus ที่ Cancel สำหรับการลบ, Escape, Tab/Shift+Tab focus trap และคืน focus ไปยัง trigger
- การลบ Custom Rule และ IP Policy เริ่ม request หลังยืนยันเท่านั้น และไม่ปิด dialog ก่อน server ยืนยันผล
- ความล้มเหลวของ create/update/delete/toggle แสดงในหน้าแบบ localized และใช้ `readApiError` โดยไม่แสดงข้อมูลลับหรือ raw exception
- รักษา visual language เดิมของ browser console; ใช้ปุ่ม danger เฉพาะ action ที่ทำลายข้อมูล และไม่เพิ่ม dependency

## Non-goals / Anti-goals

- ไม่เปลี่ยน endpoint, authorization, persistence หรือ semantics ของ Custom Rule/IP Policy
- ไม่แก้ native dialog ของ login/setup หรือ workflow ที่อยู่นอก security rules/IP policy ในโมดูลนี้
- ไม่อ้างว่าผ่าน authenticated browser smoke, PostgreSQL runtime, หรือ whole-console premium audit หากยังไม่ได้รันหลักฐานจริง
- ไม่สร้าง dialog หลายแบบที่มี focus behavior ต่างกันในแต่ละหน้า

## Approaches

1. **Shared app-owned dialog (เลือกใช้):** เพิ่ม `showAppDialog(options)` ใน `app.js` แล้วให้ Custom Rules และ IP Policy เรียกใช้ร่วมกัน ข้อดีคือ behavior/accessibility/error copy อยู่ที่เดียวและขยายต่อได้ ความเสี่ยงคือแก้ไฟล์ legacy ขนาดใหญ่ จึงต้อง pin contract ด้วย source tests และ regression
2. **แก้เฉพาะ native confirm:** ใช้ dialog แยกสองจุดและคง `alert()` ไว้ ลด diff แต่ยังมี accessibility และ feedback drift จึงไม่ตอบเป้าหมายทั้งหมด
3. **ใช้ library dialog:** ได้ primitive สำเร็จรูป แต่เพิ่ม dependency/build surface โดยไม่จำเป็นกับแอป vanilla JS และอาจทำให้ publish bundle เปลี่ยนเกินขอบเขต

## Behavior contract

| Operation | Trigger | Pending | Success | Failure |
|---|---|---|---|---|
| Delete Custom Rule | app dialog; Cancel ได้ | dialog คงอยู่, ปุ่มยืนยัน disabled ระหว่าง DELETE | ปิด dialog, reload list, status สำเร็จ | dialog คงอยู่พร้อม error และทางเลือก retry/cancel |
| Delete IP Policy | app dialog; Cancel ได้ | dialog คงอยู่, ปุ่มยืนยัน disabled ระหว่าง DELETE | ปิด dialog, reload list, status สำเร็จ | dialog คงอยู่พร้อม error และทางเลือก retry/cancel |
| Save Custom Rule / IP Policy | submit/save | ปุ่ม save disabled ระหว่าง request | reload/แสดง status สำเร็จ | inline/global status ใช้ `readApiError`, เก็บ input ไว้ |
| Toggle WAF Rule | checkbox change | checkbox disabled ระหว่าง request | refresh authoritative state | แสดง status และคืนค่า checkbox เดิม |

## Acceptance / 100% gate

- Before = 0%; after = 100% only when the source contract, RED/GREEN test, focused and relevant regression tests, JavaScript syntax, Release build, diff check, detector, and handoff evidence all pass.
- Static contract proves no native `confirm()`/`alert()` remains in the Custom Rules/IP Policy workflow and the shared dialog has required accessibility/focus hooks.
- Evidence records exact command results, commit, progress, known verification boundaries, and artifact status in `handoff.md`.
