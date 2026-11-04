# Recovery Diagnostics Lifecycle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Invalidate stale recovery diagnostics during refresh and expose copy only for the current rendered state.

**Architecture:** Keep lifecycle state in `SiteHealthSummaryRecoveryState`; the Desktop view mirrors the state by hiding/clearing its diagnostics controls at refresh start and enabling them after `RenderSummary`. Existing bounded event retention remains unchanged.

**Tech Stack:** .NET 10, C#, WPF, xUnit, PostgreSQL integration runner.

**Spec:** `docs/superpowers/specs/2026-10-26-multisite-recovery-diagnostics-lifecycle-design.md`

## Global Constraints

- Diagnostics are local-only and bounded to 256 characters.
- Event history is bounded to eight events.
- No polling, automatic retry, network call, or sensitive infrastructure detail.

## Review Focus

- Copy during an in-flight refresh must be disabled and stale text cleared.
- A successful refresh resets active failures but keeps the bounded recovery event.
- Repeated refreshes must not grow event memory or diagnostics display.

### Task 1: State lifecycle contract

**Files:**
- Modify: `src/AegisWAF.Client/SiteHealthSummaryRecoveryState.cs`
- Test: `tests/AegisWAF.Tests/SiteHealthSummaryDiagnosticsLifecycleTests.cs`

- [ ] Write failing tests for `DiagnosticsAvailable`, invalidation at `BeginRefresh`, and the 256-character bound.
- [ ] Run the focused test build and verify the expected missing-member failure.
- [ ] Implement the smallest state lifecycle API and keep `MaxEventHistory` unchanged.
- [ ] Run the focused state tests and verify green.

### Task 2: Desktop lifecycle integration

**Files:**
- Modify: `src/AegisWAF.Desktop/Views/SitesView.xaml.cs`, `src/AegisWAF.Desktop/Views/SitesView.xaml`
- Test: `tests/AegisWAF.Tests/UiContractTests.cs`

- [ ] Add a refresh-start reset for visibility, text, and copy enabled state.
- [ ] Enable and render diagnostics only after the current refresh has completed.
- [ ] Verify the UI contract and accessibility behavior.

### Task 3: Verification and handoff

**Files:**
- Create: `artifacts/module-verification/<timestamp>-multisite-module24-recovery-diagnostics-lifecycle.json`
- Modify: `handoff.md`

- [ ] Run focused PostgreSQL/API/UI tests.
- [ ] Run solution and runner builds with warnings as errors.
- [ ] Run `git diff --check`, parse the artifact, and record before/after percentages.
