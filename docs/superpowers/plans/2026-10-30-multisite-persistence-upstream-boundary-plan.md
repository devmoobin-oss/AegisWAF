# Multi-Site persistence upstream boundary Implementation Plan

**Spec:** `docs/superpowers/specs/2026-10-30-multisite-persistence-upstream-boundary-design.md`

## งาน

- [x] เพิ่ม RED regression สำหรับ persisted query/fragment upstream
- [x] ปฏิเสธ query/fragment ใน `SitePersistenceCodec.FromRow`
- [x] รัน focused persistence regression
- [x] รัน targeted Multi-Site regression, Release build และตรวจ artifact
- [x] บันทึก Module 185 และ pointer ล่าสุดใน `handoff.md`
