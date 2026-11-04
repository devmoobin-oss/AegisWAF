# Refresh Event Presentation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Make bounded cancellation events visible and filterable in the Desktop Multi-site recovery panel.

**Architecture:** Add a typed filter query to `SiteHealthSummaryRefreshLifecycle`; `SitesView` maps ComboBox tags to the closed enum and renders only safe reason labels locally.

**Tech Stack:** .NET 10, C#, WPF, xUnit, PostgreSQL integration runner.

**Spec:** `docs/superpowers/specs/2026-10-26-multisite-refresh-event-presentation-design.md`

## Global Constraints

- Only allowlisted enum reasons are filterable.
- Rendering is capped at three cancellation and three health-recovery events.
- Filter changes cause no network call.

### Task 1: Filter contract

**Files:**
- Modify: `src/AegisWAF.Client/SiteHealthSummaryRefreshLifecycle.cs`
- Test: `tests/AegisWAF.Tests/SiteHealthSummaryRefreshLifecycleTests.cs`

- [ ] Add failing tests for all/reason-specific filtering and bounded results.
- [ ] Run the focused build and verify the missing-member failure.
- [ ] Implement the typed in-memory query.
- [ ] Run filter tests and verify green.

### Task 2: Desktop presentation

**Files:**
- Modify: `src/AegisWAF.Desktop/Views/SitesView.xaml`, `src/AegisWAF.Desktop/Views/SitesView.xaml.cs`
- Test: `tests/AegisWAF.Tests/UiContractTests.cs`

- [ ] Add the accessible filter ComboBox and allowlisted tags.
- [ ] Render bounded cancellation/recovery labels and refresh on selection only.
- [ ] Verify no API call is introduced by filtering.

### Task 3: Verification and handoff

**Files:**
- Create: `artifacts/module-verification/<timestamp>-multisite-module28-refresh-event-presentation.json`
- Modify: `handoff.md`

- [ ] Run focused PostgreSQL/API/UI tests.
- [ ] Run solution/runner builds and diff verification.
- [ ] Record before/after evidence and next opportunity.
