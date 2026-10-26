# Multi-site Runtime Probe Reliability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** ทำให้ Active Probe ของ Multi-site ตรวจ loopback test upstream ได้เสถียร พร้อมรายงาน socket failure ที่แยกสาเหตุได้ โดยไม่ลดทอน Production Egress Guard

**Architecture:** รักษา `SiteHealthProbe` และ `EgressConnectionGuard` เป็น boundary เดิม เพิ่มการจำแนก exception แบบ bounded ใน probe และย้าย temporary HTTP listener ของ test ไปเป็น helper ที่ควบคุม lifetime, cancellation และ response framing อย่าง deterministic. ไม่มีการเปิด private egress ใน Production และไม่มีการเปลี่ยน alert semantics ในโมดูลนี้

**Tech Stack:** .NET 10, C#, xUnit, `SocketsHttpHandler`, `TcpListener`, PowerShell verification runner

**Spec:** `docs/superpowers/specs/2026-10-26-multisite-runtime-probe-reliability-design.md`

## Global Constraints

- Default private/loopback upstream remains denied.
- `AllowPrivateExceptions=true` remains rejected when `Waf:Deployment:Enforce=true`.
- Caller cancellation must propagate; only internal timeout becomes reason `timeout`.
- Probe failure reason text remains bounded and must not expose credentials or request bodies.
- Every production code change requires a failing test first.

## Review Focus

- A live IPv4 loopback listener on an ephemeral port must return `healthy` reliably.
- A refused socket must expose a stable bounded socket reason without leaking a full exception chain.
- Caller cancellation must not be converted into a false `unhealthy` result or swallowed.
- A denied loopback destination must remain denied when the explicit test exception is absent.
- The test server must stop cleanly without leaving an accept/read task or port behind.

### Task 1: Deterministic loopback probe server regression

**Files:**
- Modify: `tests/AegisWAF.Tests/SiteHealthProbeTests.cs`
- Test: `tests/AegisWAF.Tests/SiteHealthProbeTests.cs`

**Interfaces:**
- Produces a test-only `LoopbackHttpServer` helper used by live success, refusal, and cancellation scenarios.

- [ ] Add a failing/reproducible test helper contract that starts `TcpListener(IPAddress.Loopback, 0)`, exposes the bound `Uri`, accepts requests with cancellation, sends a framed HTTP 200 response, and disposes all resources.
- [ ] Run `SiteHealthProbeTests` and confirm the existing live-loopback case is still the failing case or that the new helper exposes the exact failure boundary.
- [ ] Replace the one-shot inline listener in `Probe_returns_healthy_for_a_live_upstream_with_an_explicit_test_exception` with the deterministic helper.
- [ ] Run `SiteHealthProbeTests` and confirm the live loopback case passes without weakening the egress policy.

### Task 2: Bounded socket failure diagnostics and cancellation contract

**Files:**
- Modify: `src/AegisWAF.Gateway/Sites/SiteHealthProbe.cs`
- Modify: `tests/AegisWAF.Tests/SiteHealthProbeTests.cs`

**Interfaces:**
- `SiteHealthProbe.ProbeAsync` continues returning `SiteHealthProbeResult`.
- Internal reason formatting must return bounded text and include a stable `SocketErrorCode` when one exists at any nested exception level.

- [ ] Add a failing test asserting a refused/failed connection reason contains the socket error code and is bounded.
- [ ] Add a failing test asserting caller cancellation throws `OperationCanceledException` with the caller token.
- [ ] Run the focused tests and confirm both new tests fail for the missing diagnostics/contract.
- [ ] Implement recursive bounded socket-exception extraction in `SiteHealthProbe` without changing egress decisions or caller cancellation handling.
- [ ] Run the focused tests and confirm the new diagnostics and cancellation tests pass.

### Task 3: Module verification and handoff

**Files:**
- Modify: `handoff.md`
- Create: `artifacts/module-verification/<timestamp>-multisite-module38-runtime-probe-reliability.json`

- [ ] Rebuild solution and in-process runner with warnings as errors.
- [ ] Run `SiteHealthProbeTests` twice consecutively and run the focused Multi-site/alert/UI regression classes.
- [ ] Run `git diff --check` and inspect the output.
- [ ] Record evidence, limitations, and Before 0% → After 100% for Module 38 only when every gate passes; if a gate remains environment-blocked, record the exact gate as unverified and keep the module below 100%.

## Self-review

- Spec coverage: listener lifecycle, bounded diagnostics, cancellation, security invariants, focused regression, build, and handoff are covered by Tasks 1–3.
- The plan does not alter alert semantics, production allowlists, or multi-instance behavior; those remain separate modules.
- The known live-socket failure is covered by Task 1 before any production change is made.
