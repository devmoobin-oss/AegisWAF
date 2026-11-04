# Module 215 — Embedded Gateway Single-Flight Startup

## Intent

ทำให้ `EmbeddedGateway` รองรับการเรียก `StartAsync` พร้อมกันอย่างปลอดภัยใน Desktop process เดียวกัน เพื่อไม่ให้การกด Retry หรือ lifecycle callback ซ้ำกันทำให้ Bootstrap/seed ชนกันบน PostgreSQL หรือทำให้ instance ที่เริ่มสำเร็จถูกหยุดโดย error path ของอีกคำขอ

## Current evidence

`StartAsync` ตรวจ `_app` ภายใต้ `lock` แล้วปล่อย lock ก่อนทำ Bootstrap และ `app.StartAsync` จึงมีหลาย startup workflow ทำงานพร้อมกันได้ การทดสอบ Module 215 ก่อนแก้ล้มด้วย `23505 waf_site_hostnames_pkey` ใน `EnsureSeededAsync` เมื่อเรียก start พร้อมกัน 4 ครั้ง

## Goals

- ให้ startup ของ `EmbeddedGateway` มีเพียงหนึ่ง workflow ต่อ instance ในเวลาเดียวกัน
- คำขอ `StartAsync` ที่เข้ามาซ้ำระหว่าง startup เดียวกันรอและจบสำเร็จเมื่อ instance แรกพร้อมแล้ว
- ให้ `StopAsync` ไม่แทรกกลาง startup และไม่หยุด instance ที่ startup อื่นเพิ่งเปิดสำเร็จ
- รักษา cancellation ของคำขอที่กำลังรอ และ cleanup app ที่ startup ล้มเหลว

## Non-goals

- ไม่เปลี่ยนพอร์ตเริ่มต้นหรือรูปแบบการตั้งค่า Desktop
- ไม่แก้ race ระหว่าง `EmbeddedGateway` คนละ object หรือคนละ process; กรณีนั้นต้องใช้ distributed PostgreSQL mutation/startup policy แยกต่างหาก
- ไม่เพิ่มการ retry แบบซ่อนปัญหา port conflict

## Approach

ใช้ lifecycle `SemaphoreSlim` เดียวครอบคลุม check/start/assignment และ stop/dispose โดยยังใช้ lock สำหรับการอ่าน/เขียน reference ของ `_app` ให้ `Running` ปลอดภัย การแก้มีขอบเขตแคบและตรงกับ race ที่ regression test จับได้

## Verification

ใช้ regression test เรียก `StartAsync` พร้อมกัน 4 ครั้งบน URL เดียวกัน แล้วตรวจว่า `Task.WhenAll` สำเร็จ, `Running` เป็นจริง และ `/health` ตอบสำเร็จ จากนั้นรัน Embedded Gateway suite, Multi-Site regression, solution build และ self-contained publish
