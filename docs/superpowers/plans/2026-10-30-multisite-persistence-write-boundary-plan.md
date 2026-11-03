# Multi-Site persistence write boundary Implementation Plan

**Spec:** `docs/superpowers/specs/2026-10-30-multisite-persistence-write-boundary-design.md`

## งาน

- [x] เพิ่ม RED test สำหรับ `SitePersistenceCodec.ToRow`
- [x] เพิ่ม write-side query/fragment guard ก่อนสร้าง row
- [x] รัน focused persistence tests
- [x] รัน targeted Multi-Site regression, Release build และตรวจ artifact
- [x] บันทึก Module 186 และ pointer ล่าสุดใน `handoff.md`
