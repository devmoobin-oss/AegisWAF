# Refresh Result Ordering Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Suppress late results from superseded Multi-site refreshes.

**Architecture:** Extend `SiteHealthSummaryRefreshLifecycle` with a monotonic generation and current-generation predicate. `SitesView` captures the generation and checks it after API awaits, before state mutation, and on error paths.

**Tech Stack:** .NET 10, C#, WPF, xUnit, PostgreSQL integration runner.

**Spec:** `docs/superpowers/specs/2026-10-26-multisite-summary-refresh-generation-design.md`

## Global Constraints

- Only the newest generation may render.
- No polling, retry loop, network endpoint, or sensitive data in the guard.
- Generation must not wrap.

### Task 1: Generation contract

**Files:**
- Modify: `src/AegisWAF.Client/SiteHealthSummaryRefreshLifecycle.cs`
- Test: `tests/AegisWAF.Tests/SiteHealthSummaryRefreshLifecycleTests.cs`

- [ ] Add failing tests for superseded generation, invalidation, and saturation.
- [ ] Run the focused build and verify missing-member failure.
- [ ] Implement the monotonic generation predicate.
- [ ] Run lifecycle tests and verify green.

### Task 2: View integration

**Files:**
- Modify: `src/AegisWAF.Desktop/Views/SitesView.xaml.cs`
- Test: `tests/AegisWAF.Tests/UiContractTests.cs`

- [ ] Capture generation per refresh.
- [ ] Check generation after each await and before success/error rendering.
- [ ] Invalidate generation on unload and verify the contract.

### Task 3: Verification and handoff

**Files:**
- Create: `artifacts/module-verification/<timestamp>-multisite-module26-summary-refresh-generation.json`
- Modify: `handoff.md`

- [ ] Run focused PostgreSQL/API/UI tests.
- [ ] Run solution/runner builds and diff verification.
- [ ] Record before/after evidence and next opportunity.
