# Event Timeline Ordering Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Make local cancellation event order deterministic under clock skew.

**Architecture:** Extend `RefreshCancellationEvent` with a saturating sequence assigned at insertion. The lifecycle accepts an injectable clock for deterministic tests, but production defaults to UTC; filtering explicitly orders snapshots by sequence.

**Tech Stack:** .NET 10, C#, WPF, xUnit, PostgreSQL integration runner.

**Spec:** `docs/superpowers/specs/2026-10-26-multisite-event-timeline-clock-skew-design.md`

## Global Constraints

- Sequence, not timestamp, determines presentation order.
- History remains bounded to eight and display to three.
- No network or sensitive data changes.

### Task 1: Sequence contract

**Files:**
- Modify: `src/AegisWAF.Client/SiteHealthSummaryRefreshLifecycle.cs`
- Test: `tests/AegisWAF.Tests/SiteHealthSummaryRefreshLifecycleTests.cs`

- [ ] Add failing tests for backward timestamps and sequence ordering/saturation.
- [ ] Run focused build and verify missing-member failure.
- [ ] Implement sequence assignment, injectable clock, and sequence-ordered snapshots.
- [ ] Run ordering tests and verify green.

### Task 2: Desktop presentation

**Files:**
- Modify: `src/AegisWAF.Desktop/Views/SitesView.xaml.cs`
- Test: `tests/AegisWAF.Tests/UiContractTests.cs`

- [ ] Render safe sequence labels while preserving bounded filter behavior.
- [ ] Verify no timestamp-based sort or network request is introduced.

### Task 3: Verification and handoff

**Files:**
- Create: `artifacts/module-verification/<timestamp>-multisite-event-timeline-clock-skew.json`
- Modify: `handoff.md`

- [ ] Run focused PostgreSQL/API/UI tests.
- [ ] Run solution/runner builds and diff verification.
- [ ] Record before/after evidence and next opportunity.
