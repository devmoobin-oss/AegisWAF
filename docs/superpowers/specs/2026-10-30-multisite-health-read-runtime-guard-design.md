# Multi-Site health read runtime guard

## Intent

ทำให้ health history และ trend สะท้อน route ที่กำลังทำงานจริง ไม่แสดงข้อมูล Alert จาก Site configuration ที่บันทึกไว้แต่ยังไม่ active ใน Gateway runtime

## Goals

- history/trend ตรวจ persisted-versus-active state ด้วย guard เดียวกับ Site listing, manual health และ acknowledgement
- เมื่อ pending restart ให้คืน `pendingRestart=true`, `activationStatus=pending-restart`, `historyRetention=none` และไม่คำนวณจาก history เก่า
- คงการ fallback เมื่อ database อ่านไม่ได้สำหรับ Site ที่ active อยู่

## Non-goals

- ไม่ activate route อัตโนมัติ
- ไม่ลบหรือแก้ไขประวัติใน PostgreSQL
- ไม่เปลี่ยน role, CSRF, timeout หรือ acknowledgement write policy

## Approach

ตรวจ `SiteRuntimeState.IsPendingRestart(site, active)` ทันทีหลัง resolve Site และก่อนอ่าน/merge history ในทั้งสอง endpoint โดยตอบ envelope ที่บอกสถานะ pending อย่างชัดเจนและใช้ข้อมูลว่างแทน Alert เก่า

## Verification boundary

ใช้ contract tests แยก endpoint, targeted Multi-Site regression, Release build, JavaScript syntax check และ publish manifest verification ตาม workflow เดิม
