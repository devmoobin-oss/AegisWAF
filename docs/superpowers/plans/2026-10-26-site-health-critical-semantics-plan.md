# Site Health Critical Semantics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** ป้องกัน false-critical ใน Multi-Site โดยคำนวณ failure streak ตามรอบตรวจของ Site และจำนวน upstream ที่คาดหมาย

**Architecture:** `SiteHealthProbe` จะใช้ `CheckedAt` เดียวต่อ probe batch. `SiteHealthTrendCalculator` จะ normalize timestamp, group observations เป็น batch, ตรวจ batch completeness และคำนวณ alert state จาก site-level failure streak. Production callers จะส่ง expected upstream count จาก `GatewaySite`; legacy callers ใช้ fallback จาก distinct upstream IDs.

**Tech Stack:** C#/.NET 10, xUnit, ASP.NET Gateway, PostgreSQL health history.

**Spec:** `docs/superpowers/specs/2026-10-26-site-health-critical-semantics-design.md`

## Global Constraints

- Critical threshold remains exactly 3 consecutive site-failed batches.
- Existing observation counts and API fields remain compatible.
- Partial or incomplete upstream batches must not become critical solely from missing data.
- No raw exception, credential, connection string, SQL, or host detail may be added to responses.
- Production probe timestamps must be shared by all upstreams in one probe invocation.

## Review Focus

- Two upstreams failing in one batch must count as one failed round, not two.
- A batch with one healthy and one unhealthy upstream must remain degraded.
- A batch missing an expected upstream must not be critical.
- Three complete all-unhealthy batches must still become critical.
- Latest complete healthy batch after failures must retain history and report recovered.

---

### Task 1: Lock the false-critical behavior with tests

**Files:**
- Modify: `tests/AegisWAF.Tests/SiteHealthTrendTests.cs`
- Modify: `tests/AegisWAF.Tests/SiteHealthAlertTests.cs`

**Interfaces:**
- Consumes: existing `SiteHealthTrendCalculator.Calculate` and `SiteHealthComparisonCalculator.Calculate`.
- Produces: failing regression tests for batch-aware semantics.

- [ ] **Step 1: Add RED tests**
  - Two upstream failures sharing one timestamp produce `ConsecutiveUnhealthy=1`, not `2`.
  - Three complete all-unhealthy batches with expected count 2 produce `critical` and streak 3.
  - Partial healthy/unhealthy batch remains `degraded`.
  - Incomplete batch with expected count 2 does not become critical.
  - Existing single-upstream three-failure test remains critical.
- [ ] **Step 2: Run focused tests and verify they fail for the missing behavior**
  - Run `dotnet test tests/AegisWAF.Tests/AegisWAF.Tests.csproj --no-restore --filter FullyQualifiedName~SiteHealthTrendTests`.
  - If the repository SDK resolver blocks test execution, record the exact failure and use the project build/runtime checks without calling the tests passed.

### Task 2: Make probe batches deterministic

**Files:**
- Modify: `src/AegisWAF.Gateway/Sites/SiteHealthProbe.cs`
- Test: `tests/AegisWAF.Tests/SiteHealthProbeTests.cs` or a focused contract test beside existing probe tests.

**Interfaces:**
- Consumes: `SiteHealthProbe.ProbeAsync`.
- Produces: all results in one invocation share one `CheckedAt` value.

- [ ] **Step 1: Add a test asserting one probe invocation uses one timestamp for all upstream results.**
- [ ] **Step 2: Run the test and verify RED.**
- [ ] **Step 3: Capture `DateTimeOffset.UtcNow` once before the upstream loop and reuse it.**
- [ ] **Step 4: Run the focused probe test and verify GREEN.**

### Task 3: Implement batch-aware trend calculation

**Files:**
- Modify: `src/AegisWAF.Gateway/Sites/SiteHealthTrend.cs`
- Modify: `src/AegisWAF.Gateway/Sites/SiteHealthAlert.cs`
- Modify: `src/AegisWAF.Gateway/GatewayApp.cs`

**Interfaces:**
- Consumes: observations, `expectedUpstreamCount`, and existing API callers.
- Produces: `SiteHealthTrendCalculator.Calculate(observations, window, expectedUpstreamCount = 0)` and an optional expected-count map on comparison calculation.

- [ ] **Step 1: Implement timestamp normalization and batch grouping.**
- [ ] **Step 2: Calculate complete site-failed batches and use them for `ConsecutiveUnhealthy`/`critical`.**
- [ ] **Step 3: Preserve observation counts and recovery/stale behavior.**
- [ ] **Step 4: Pass `site.Upstreams.Count` from direct trend/acknowledgement/summary paths.**
- [ ] **Step 5: Run the focused trend/alert tests and verify GREEN.**

### Task 4: Integration and evidence

**Files:**
- Create: `artifacts/module-verification/<timestamp>-module50-site-health-critical-semantics-final.json`
- Modify: `HANDOFF.md`

- [ ] **Step 1: Build Gateway and affected projects.**
- [ ] **Step 2: Run focused regression and the available full verification command.**
- [ ] **Step 3: Run a runtime/API check showing complete-failure, partial-failure, and recovery semantics.**
- [ ] **Step 4: Run `git diff --check`.**
- [ ] **Step 5: Write Module 50 Before/After progress and next opportunity audit to `HANDOFF.md`.**

## Completion Checklist

- [ ] RED evidence exists for the false-critical regression.
- [ ] GREEN evidence covers all five review-focus cases.
- [ ] Production callers provide expected upstream count.
- [ ] Gateway build and runtime evidence pass.
- [ ] Artifact and `HANDOFF.md` record Before 0%, After 100%, Target 100% only when all required evidence is present.
