# Refresh Cancellation Taxonomy Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add bounded, safe cancellation reason observability to the Multi-site refresh lifecycle.

**Architecture:** Extend `SiteHealthSummaryRefreshLifecycle` with a closed reason enum, bounded event queue, saturating count, and `Complete(token)`. `SitesView` passes the intent reason, completes refreshes, and includes only the safe last reason in diagnostics.

**Tech Stack:** .NET 10, C#, WPF, xUnit, PostgreSQL integration runner.

**Spec:** `docs/superpowers/specs/2026-10-26-multisite-refresh-cancellation-taxonomy-design.md`

## Global Constraints

- Real cancellation only; completed refreshes do not create events.
- No sensitive data, polling, retry loop, or new endpoint.
- History is bounded to eight events and count saturates.

### Task 1: Taxonomy and lifecycle contract

**Files:**
- Modify: `src/AegisWAF.Client/SiteHealthSummaryRefreshLifecycle.cs`
- Test: `tests/AegisWAF.Tests/SiteHealthSummaryRefreshLifecycleTests.cs`

- [ ] Add failing tests for reason recording, completion, bounded history, and no false cancellation after completion.
- [ ] Run the focused build and verify the missing-type/member failure.
- [ ] Implement the closed enum, bounded event history, `Complete`, and safe counters.
- [ ] Run lifecycle tests and verify green.

### Task 2: Diagnostics and Desktop integration

**Files:**
- Modify: `src/AegisWAF.Client/SiteHealthSummaryRecoveryState.cs`, `src/AegisWAF.Desktop/Views/SitesView.xaml.cs`
- Test: `tests/AegisWAF.Tests/SiteHealthSummaryDiagnosticsTests.cs`, `tests/AegisWAF.Tests/UiContractTests.cs`

- [ ] Include only the safe last reason in diagnostics.
- [ ] Pass retry/superseded/unloaded intent and complete each refresh.
- [ ] Verify cancellation does not increment summary failure counters.

### Task 3: Verification and handoff

**Files:**
- Create: `artifacts/module-verification/<timestamp>-multisite-module27-refresh-cancellation-taxonomy.json`
- Modify: `handoff.md`

- [ ] Run focused PostgreSQL/API/UI tests.
- [ ] Run solution/runner builds and diff verification.
- [ ] Record before/after evidence and the next opportunity.
