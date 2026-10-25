# Opaque Content Inspection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** ตรวจ text-like opaque request/response content ภายใน budget เพื่อลด binary content-type bypass โดยไม่ทำลาย binary compatibility

**Architecture:** เพิ่ม `ContentInspection` classifier ที่ตรวจ UTF-8/control-byte boundedness แล้วเรียกใช้จาก request middleware และ `ResponseInspector`. Rule/response engines เดิมยังเป็นผู้ตัดสิน block; classifier เพียงบอกว่าควรส่ง bytes เข้า engine หรือไม่.

**Tech Stack:** C#/.NET 10, ASP.NET middleware, xUnit, existing fake-upstream integration tests, module verifier.

**Spec:** `docs/superpowers/specs/2026-10-25-opaque-content-inspection-design.md`

## Global Constraints

- ไม่ parse proprietary binary formats และไม่ตรวจ response ที่เกิน cap ในโหมด stream เดิม
- opaque content ที่เป็น valid UTF-8 และไม่มี binary controls ต้องเข้า rule/leak engine เดิม
- ต้องคง behavior ของ binary จริงและ content types ที่รู้จัก
- ต้องมี build, focused request/response tests, documentation contract และ full-suite evidence

## Review Focus

- Opaque request ที่เป็นข้อความโจมตี — ต้องถูก block (`CatalogVerificationTests`)
- Opaque response ที่เป็น text leak — ต้องตอบ 502 และไม่ปล่อยข้อมูล (`EngineUpgradeTests`)
- Opaque body ที่มี NUL/control bytes — ต้องไม่ถูก decode เป็น text (classifier regression)
- Invalid UTF-8 — ต้องไม่ throw หรือทำให้ middleware ล้ม
- Response ที่เกิน cap — ต้องคง overflow policy เดิมและไม่ถูกนับว่าโมดูลนี้ปิดช่องแล้ว

---

### Task 1: Add failing request/response regression tests

**Files:**
- Modify: `tests/AegisWAF.Tests/CatalogVerificationTests.cs`
- Modify: `tests/AegisWAF.Tests/EngineUpgradeTests.cs`

- [ ] **Step 1: Change the opaque request example** to expect the existing XSS payload to be blocked.
- [ ] **Step 2: Change the opaque response leak test** to expect 502 and verify the secret is not returned.
- [ ] **Step 3: Run the two focused tests** and confirm they fail against the current fail-open behavior.

### Task 2: Implement bounded text-like classification

**Files:**
- Create: `src/AegisWAF.Gateway/ContentInspection.cs`
- Modify: `src/AegisWAF.Gateway/GatewayApp.cs`
- Modify: `src/AegisWAF.Gateway/Program.cs`

**Interfaces:**
- Produces: `ContentInspection.IsOpaqueContentType(string?)`, `ContentInspection.IsTextLike(ReadOnlySpan<byte>)`, and `ResponseInspector.IsInspectable(string?, ReadOnlySpan<byte>)`

- [ ] **Step 1: Implement strict UTF-8 and bounded control-byte classification.**
- [ ] **Step 2: Include opaque request bodies in the existing bounded inspection path** and only decode text-like bytes.
- [ ] **Step 3: Pass captured response bytes to `ResponseInspector`** before leak inspection.
- [ ] **Step 4: Run focused tests and add binary/invalid-UTF8 classifier coverage.**

### Task 3: Align catalog and documentation

**Files:**
- Modify: `src/AegisWAF.Client/ProtectionCatalog.cs`
- Modify: `docs/PROTECTION.md`, `docs/SECURITY.md`, `docs/PROGRESS.md`, `docs/DEVELOPMENT_ROADMAP.md`

- [ ] **Step 1: Replace stale claims that all octet-stream content bypasses inspection.**
- [ ] **Step 2: Document remaining limits: real binary, invalid UTF-8, and responses over cap.**

### Task 4: Add module verification and handoff

**Files:**
- Modify: `tools/module-verification/modules.json`
- Modify: `handoff.md`

- [ ] **Step 1: Add module 10 with build, request tests, response tests and docs contract gates.**
- [ ] **Step 2: Record 0% -> final percentage and evidence.**

### Task 5: Full verification and commit

- [ ] **Step 1: Run full suite and capture counts.**
- [ ] **Step 2: Run all-module verifier and confirm every module is >98%.**
- [ ] **Step 3: Review diff, commit scoped changes, and verify clean status.**
