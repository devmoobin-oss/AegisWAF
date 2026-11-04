# Multi-site Recovery Timeline Export Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** เพิ่มการ export timeline ของ Multi-site recovery แบบ bounded, redacted และ local-only เพื่อการ support handoff

**Architecture:** เพิ่ม pure exporter ใน `AegisWAF.Client` ที่รับ safe domain events และสร้างข้อความจาก allowlist โดยไม่รับข้อมูล transport หรือ Site identity จากนั้นให้ `SitesView` ใช้ exporter กับเหตุการณ์ที่กรองแล้วและคัดลอกผ่าน clipboard เท่านั้น

**Tech Stack:** .NET 10, C#, xUnit, WPF

**Spec:** `docs/superpowers/specs/2026-10-26-multisite-recovery-timeline-export-design.md`

## Global Constraints

- Output ต้องไม่เกิน 1024 ตัวอักษร
- Export cancellation ได้เฉพาะ reason ใน `RefreshCancellationReason` ที่รู้จัก
- Export recovery ได้เฉพาะ kind `failure` และ `recovered`
- ไม่เรียก API เพิ่มและไม่ส่งข้อมูลออกนอกเครื่อง
- เก็บประวัติใน memory ตาม bounded history เดิม

## Review Focus

- Timestamp ย้อนกลับ: export ต้องใช้ sequence ของ cancellation ไม่ใช่ timestamp
- Unknown enum/kind: ต้อง redacted และไม่สะท้อน raw value
- Input จำนวนมาก/ข้อความยาว: output ต้องไม่เกิน bound
- ไม่มี event: UI ต้องซ่อน/disable ปุ่มอย่างปลอดภัย
- Filter เปลี่ยน: export ต้องใช้ filter ปัจจุบันโดยไม่เพิ่ม failure telemetry หรือ network call

---

### Task 1: Define exporter contract with failing tests

**Files:**
- Create: `tests/AegisWAF.Tests/SiteHealthSummaryTimelineExporterTests.cs`
- Create: `src/AegisWAF.Client/SiteHealthSummaryTimelineExporter.cs`

**Interfaces:**
- Produces `SiteHealthSummaryTimelineExporter.Export(IEnumerable<RefreshCancellationEvent>, IEnumerable<SiteHealthSummaryRecoveryEvent>, RefreshCancellationReason, int)` returning `string`

- [ ] **Step 1: Write tests** for deterministic sequence ordering/filtering, allowlist redaction, and 1024-character bound.
- [ ] **Step 2: Run focused tests** and confirm they fail because the exporter is not implemented.
- [ ] **Step 3: Implement** the smallest pure exporter with safe labels, `TakeLast(3)` per event type, sequence ordering, and bounded output.
- [ ] **Step 4: Run focused tests** and confirm they pass.

### Task 2: Integrate Desktop copy behavior

**Files:**
- Modify: `src/AegisWAF.Desktop/Views/SitesView.xaml`
- Modify: `src/AegisWAF.Desktop/Views/SitesView.xaml.cs`
- Modify: `tests/AegisWAF.Tests/UiContractTests.cs`

**Interfaces:**
- Consumes the exporter from Task 1 and the existing selected cancellation reason.
- Produces a local clipboard action with `CopyRecoveryTimelineButton` and `OnCopyRecoveryTimeline`.

- [ ] **Step 1: Add UI contract assertions** for the button, handler, local exporter call, and disabled lifecycle.
- [ ] **Step 2: Run the UI contract test** and confirm it fails.
- [ ] **Step 3: Add the button and lifecycle-safe handler**; keep it disabled until a safe timeline exists.
- [ ] **Step 4: Run focused UI/export tests** and confirm they pass.

### Task 3: Verify and hand off

**Files:**
- Modify: `handoff.md`
- Create: `artifacts/module-verification/20261026-103000-multisite-module30-recovery-timeline-export.json`

- [ ] **Step 1: Run focused PostgreSQL/API/UI gate, solution build, and diff check.**
- [ ] **Step 2: Record before/after percentages, exact commands, counts, and known full-regression baseline in the artifact and `handoff.md`.**
- [ ] **Step 3: Audit the next opportunity and document Module 31.
