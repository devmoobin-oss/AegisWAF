# Summary Refresh Cancellation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Make Multi-site refreshes cancellation-safe across navigation and superseding refreshes.

**Architecture:** A small shared `SiteHealthSummaryRefreshLifecycle` owns one current `CancellationTokenSource`. `SitesView` begins a token per refresh, passes it into both API calls, and cancels it from `Unloaded` or when a newer refresh starts.

**Tech Stack:** .NET 10, C#, WPF, xUnit, PostgreSQL integration runner.

**Spec:** `docs/superpowers/specs/2026-10-26-multisite-summary-refresh-cancellation-design.md`

## Global Constraints

- Cancellation is normal lifecycle control, not an operator-visible API error.
- No polling, automatic retry, or new endpoint.
- Unloaded controls remain disabled and receive no post-cancellation updates.

### Task 1: Lifecycle contract

**Files:**
- Create: `src/AegisWAF.Client/SiteHealthSummaryRefreshLifecycle.cs`
- Test: `tests/AegisWAF.Tests/SiteHealthSummaryRefreshLifecycleTests.cs`

- [ ] Write failing tests for superseding refresh cancellation and explicit cancel invalidation.
- [ ] Run the focused test build and verify the missing-type failure.
- [ ] Implement one current cancellable token source with bounded ownership.
- [ ] Run lifecycle tests and verify green.

### Task 2: Desktop integration

**Files:**
- Modify: `src/AegisWAF.Desktop/Views/SitesView.xaml`, `src/AegisWAF.Desktop/Views/SitesView.xaml.cs`
- Test: `tests/AegisWAF.Tests/UiContractTests.cs`

- [ ] Pass the token to Site and summary API calls.
- [ ] Ignore cancellation without rendering an error.
- [ ] Wire `Unloaded` to cancel and disable retry/copy controls.
- [ ] Verify the UI contract.

### Task 3: Verification and handoff

**Files:**
- Create: `artifacts/module-verification/<timestamp>-multisite-module25-summary-refresh-cancellation.json`
- Modify: `handoff.md`

- [ ] Run focused PostgreSQL/API/UI tests.
- [ ] Run solution and runner builds with warnings as errors.
- [ ] Run `git diff --check`, parse evidence, and record before/after percentages.
