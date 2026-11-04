# Multi-Site health summary runtime truth

## Intent

ทำให้ภาพรวม Health Summary ไม่ยกระดับประวัติของ Site ที่ยัง pending restart เป็น Alert ปัจจุบัน และส่งสถานะดังกล่าวไปยัง Browser/Desktop อย่างชัดเจน

## Goals

- Summary ใช้ `SiteRuntimeState.IsPendingRestart` กับทุก Site ที่อ่านจาก persistence
- ไม่รวม pending Site ในจำนวน critical/degraded/healthy/no-data/recovered ที่สะท้อน runtime health
- ส่ง `pendingRestart`, `activationStatus` ต่อรายการ และ `pendingRestartSites` ใน aggregate
- Browser/Desktop แสดงจำนวน Site ที่รอเปิดใช้โดยไม่แสดงเป็นวิกฤต

## Non-goals

- ไม่ activate, mutate หรือ delete Site
- ไม่ลบประวัติ PostgreSQL
- ไม่เปลี่ยน timeout, role, CSRF หรือ database fallback policy

## Approach

คง Site pending ไว้ใน `sites` response เพื่อไม่ให้รายการหาย แต่ override trend/Alert fields เป็น `pending-restart`, กรองออกจาก `SiteHealthSummaryAlertCounts`, และเพิ่ม metadata ใหม่สำหรับ client ทั้งสองแบบ

## Verification boundary

ใช้ contract tests สำหรับ Gateway, Browser, Desktop และ DTO, targeted Multi-Site regression, Release build, JavaScript syntax check และ publish manifest verification
