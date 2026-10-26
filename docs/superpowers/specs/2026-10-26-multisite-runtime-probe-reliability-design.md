# Multi-site Runtime Probe Reliability Design

## Intent

ทำให้ Active Probe ของ Multi-site ตรวจ upstream จริงได้เสถียรทั้งปลายทางภายนอกและปลายทางทดสอบบนเครื่องเดียวกัน โดยให้ความล้มเหลวแยกได้ว่าเกิดจาก policy, DNS, timeout, HTTP status หรือการเชื่อมต่อ TCP และไม่ลดทอน Egress Guard ของ Production

## Goals

- ให้ probe ใช้ TCP listener ชั่วคราวใน automated test ได้จริงบน Windows และไม่พึ่งพาพอร์ตคงที่
- ทำให้การเชื่อมต่อของ probe มี cancellation/timeout ที่ชัดเจนและไม่ทิ้ง socket หรือ task ค้าง
- รักษา default ที่ปฏิเสธ loopback/private target และอนุญาตเฉพาะ `AllowPrivateExceptions=true` ใน test/development ที่ Production validator ปฏิเสธ
- ส่ง reason code ที่คงที่และปลอดภัยสำหรับ operator โดยไม่เปิดเผยข้อมูลลับ
- เพิ่ม regression tests ที่ตรวจทั้ง success, cancellation, connection refusal และ policy denial

## Non-goals

- ไม่เปิด private/loopback egress ใน Production
- ไม่เปลี่ยนเกณฑ์ `critical/degraded/healthy` ของ historical alert ใน Module นี้
- ไม่เพิ่ม retry อัตโนมัติที่อาจทำให้ probe เพิ่มภาระ upstream หรือทำให้ผลตรวจช้ากว่าขอบเขต timeout
- ไม่เปลี่ยน protocol ของ upstream จาก HTTP(S)

## Constraints

- ใช้ .NET 10, `SocketsHttpHandler` และ `EgressConnectionGuard` ที่มีอยู่
- ต้องเข้ากันได้กับ Windows self-contained Desktop และ standalone Gateway
- ต้องไม่ใช้พอร์ตคงที่ใน test
- Test listener ต้องตอบ HTTP แบบขั้นต่ำและปิด connection อย่าง deterministic
- เมื่อ probe ถูกยกเลิกโดย caller ต้องรักษา cancellation semantics ของ caller; timeout ภายในต้องแปลงเป็น `timeout`

## Approaches

1. แก้เฉพาะ test ให้รอ listener/accept แบบเดิม — เปลี่ยนน้อย แต่ไม่แก้ diagnostics และยังเสี่ยง race ระหว่าง listener readiness กับ HttpClient connect
2. แก้เฉพาะ runtime probe — อาจช่วย production diagnostics แต่ไม่รับประกัน test listener lifecycle และอาจซ่อนปัญหา environment
3. แยก connection boundary ที่ทดสอบได้, จัดการ lifetime ของ temporary listener ให้ deterministic และเพิ่ม reason taxonomy/diagnostics tests — เลือกแนวทางนี้ เพราะแก้ทั้งจุดที่ fail และทำให้หลักฐาน 100% ตรวจซ้ำได้ โดยไม่ผ่อนคลาย security policy

## Design

- เพิ่ม internal/testable helper สำหรับการเชื่อมต่อ probe หรือ listener lifecycle โดยไม่ให้ test ใช้ implementation details ของ `HttpClient` มากเกินไป
- ให้ `SiteHealthProbe` แยก reason code สำหรับ egress denial, timeout, cancellation และ socket failure พร้อมคงความยาวข้อความจำกัด
- ให้ test ใช้ `TcpListener` ที่ `IPAddress.Loopback:0`, ยืนยันพอร์ตหลัง `Start()`, รับ request หนึ่งครั้ง และปิดทั้ง listener/client/stream ใน `finally`
- เพิ่ม test สำหรับ caller cancellation เพื่อยืนยันว่า probe ไม่เปลี่ยนเป็น healthy หรือกลืน cancellation

## Success criteria

- `SiteHealthProbeTests` ผ่าน 4/4 ใน environment นี้อย่างน้อยสองครั้งติดต่อกัน
- focused Multi-site/UI/alert tests ผ่านโดยไม่มี regression
- solution และ test runner build ผ่าน 0 warning/0 error
- `git diff --check` ไม่มี error
- Production validation test ยังคงปฏิเสธ private exception
- มี evidence artifact และบันทึก Before/After ใน `handoff.md` เป็น 0% → 100%
