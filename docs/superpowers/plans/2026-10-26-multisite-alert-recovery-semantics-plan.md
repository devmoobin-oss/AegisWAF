# Multi-site Alert Recovery Semantics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** แยกสถานะ historical alert ออกจากสถานะ recovered เพื่อให้ผู้ดูแลเห็นว่า Site กลับมาปกติแล้วโดยไม่ลบประวัติ

**Architecture:** เพิ่ม `RecoveryState` เป็น field ที่ backward-compatible ใน trend/comparison/API DTO และคำนวณจาก observation ล่าสุดกับ failure ที่เก่ากว่า. Desktop ใช้ recovery state เป็นสถานะ operator-facing แต่ยังแสดง historical percentage/count และคง acknowledgement ผูกกับ `AlertState` เดิม

**Tech Stack:** .NET 10, C#, WPF, xUnit, ASP.NET Minimal API

**Spec:** `docs/superpowers/specs/2026-10-26-multisite-alert-recovery-semantics-design.md`

## Global Constraints

- Do not delete or rewrite health observations.
- Keep `critical/degraded/healthy/no-data` historical alert semantics unchanged.
- Add new fields with safe defaults for older API payloads.
- Recovered Sites are not actionable for acknowledgement.
- No new polling or API fan-out.

## Review Focus

- Newest healthy observation after old failures must show recovered, not critical/degraded to the operator.
- Newest unhealthy/unknown must not be marked recovered.
- All-healthy and no-data histories must remain none.
- Summary counts must not double-count recovered as degraded.
- Older clients/payloads without `recoveryState` must deserialize as `none`.

### Task 1: Trend recovery calculation

**Files:**
- Modify: `src/AegisWAF.Gateway/Sites/SiteHealthTrend.cs`
- Modify: `tests/AegisWAF.Tests/SiteHealthTrendTests.cs`

**Interfaces:**
- `SiteHealthTrend.RecoveryState` is appended with default `none`.
- `SiteHealthTrendCalculator.Calculate` keeps the same signature.

- [ ] Add a failing test for failure history followed by a newest healthy observation: historical `AlertState` remains `degraded`, `RecoveryState` is `recovered`.
- [ ] Add tests for all-healthy, no-data, and newest unhealthy/unknown returning `RecoveryState=none`.
- [ ] Run `SiteHealthTrendTests` and verify the new recovery assertion fails before implementation.
- [ ] Implement the bounded recovery calculation without changing historical counts or thresholds.
- [ ] Run `SiteHealthTrendTests` and verify all tests pass.

### Task 2: API and Desktop contract

**Files:**
- Modify: `src/AegisWAF.Client/Models.cs`
- Modify: `src/AegisWAF.Gateway/GatewayApp.cs`
- Modify: `src/AegisWAF.Desktop/Views/SitesView.xaml.cs`
- Modify: `tests/AegisWAF.Tests/UiContractTests.cs`

**Interfaces:**
- API trend/summary objects expose `recoveryState` and summary exposes `recoveredSites`.
- Desktop `SiteHealthTrendDto` consumes the new optional field with default `none`.

- [ ] Add a failing UI contract assertion for recovery-state mapping, Thai recovered copy, green operator state, and no acknowledgement action.
- [ ] Add a failing contract assertion that the Gateway summary projection includes `recoveryState` and `recoveredSites`.
- [ ] Run focused UI/contract tests and verify failure before implementation.
- [ ] Implement API projection, DTO defaults, summary count exclusion, and Desktop recovered rendering.
- [ ] Run focused API/UI contract tests and verify all pass.

### Task 3: Verification and handoff

**Files:**
- Modify: `handoff.md`
- Create: `artifacts/module-verification/<timestamp>-multisite-module39-alert-recovery.json`

- [ ] Rebuild solution and test runner with warnings as errors.
- [ ] Run trend, alert, UI, and Multi-site focused regression tests.
- [ ] Run `git diff --check`.
- [ ] Record Before 0% → After 100% only after all Module 39 gates pass, including evidence of unchanged historical alert semantics.

## Self-review

- Spec coverage: recovery calculation, API compatibility, summary counts, Desktop presentation, tests, build, and handoff are covered by Tasks 1–3.
- The plan intentionally leaves multi-instance route activation and external production gates for later modules.
