# Opaque Content Inspection Design

## Intent

ลด bypass ที่เกิดจากการประกาศ request/response เป็น `application/octet-stream` ทั้งที่ payload จริงเป็นข้อความ เช่น XSS หรือข้อมูล `passwd` ที่รั่วจาก upstream โดยยังรักษาความเข้ากันได้กับไฟล์ไบนารีจริง

## Goals

- ตรวจ opaque request body ที่อยู่ใน inspection budget เมื่อ bytes เป็น valid UTF-8 และไม่มี control bytes ที่บ่งชี้ binary จริง
- ตรวจ opaque upstream response ด้วย heuristic เดียวกันก่อนส่งให้ client
- บล็อก payload อันตรายจาก opaque request ด้วย rule engine เดิม และบล็อก opaque response leak ด้วย `ResponseInspector` เดิม
- รักษา behavior ของ binary bytes จริง, responses ที่เกิน cap และ content types ที่รู้จักอยู่แล้ว
- เพิ่ม regression tests, เอกสาร protection claims และ module verifier gate

## Non-goals

- ถอดรหัส/parse proprietary binary formats หรือ file upload ทุกชนิด
- ตรวจ response ที่เกิน `Waf:MaxResponseInspectBytes` ในโหมด stream เดิม
- เปลี่ยน signature set, normalization หรือ response leak patterns

## Approaches

1. **ตรวจทุก bytes เป็น UTF-8** — ปิด bypass ได้มากแต่เสี่ยง false positive กับไฟล์ไบนารีและทำให้ upload เสียหาย
2. **ตรวจเฉพาะ content type ที่รู้จัก** — ปลอดภัยต่อ compatibility แต่คง bypass เดิมไว้
3. **Bounded text-like classification (เลือกใช้)** — ตรวจ opaque content เฉพาะเมื่อ valid UTF-8 และไม่มี NUL/control-byte density สูง; reuse rule/response engines เดิมและจำกัดผลกระทบไว้ใน inspection budget

## Security decisions

- `application/octet-stream` จะถูกอ่านภายใน `Waf:MaxBodyInspectBytes` เท่านั้น; เกิน budget ใช้ fail-closed behavior ของ inspection body limit
- body ที่มี NUL, invalid UTF-8 หรือ control-byte density สูงถือเป็น binary และส่งต่อได้ตาม compatibility behavior
- opaque text ที่มี payload อันตรายต้องถูกบล็อกเหมือน `text/plain`
- opaque response ที่เป็น text-like และมี leak pattern ต้องตอบ 502; binary จริงยังไม่ถูก decode เป็น text
- response ที่เกิน cap ยังขึ้นกับ `Waf:ResponseOverflowAction` เดิม; โมดูลนี้ไม่อ้างว่าปิดช่องนั้น

## Constraints

- ต้อง test-first ก่อน production code
- ไม่เพิ่ม dependency
- ต้องคง full suite และ module verifier ให้ผ่าน และบันทึกเปอร์เซ็นต์ก่อน–หลังใน `handoff.md`
