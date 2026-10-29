# Multi-Site health summary runtime truth Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the health summary truthful when persisted Site configuration is newer than the active Gateway route.

**Architecture:** The summary endpoint resolves active runtime snapshots, records pending Site IDs, excludes them from Alert counts, and projects explicit pending fields. Client DTOs and Browser/Desktop copy consume the new metadata without changing existing active-site behavior.

**Tech Stack:** ASP.NET Minimal APIs, C# records/xUnit contract tests, WPF, browser JavaScript, self-contained .NET publish.

**Spec:** `docs/superpowers/specs/2026-10-30-multisite-health-summary-runtime-truth-design.md`

## Global Constraints

- Keep pending Sites in the summary list; do not silently drop them.
- Do not mutate or auto-activate runtime configuration.
- Report Module 180 as `0% → 100%` only after red/green tests, regression, build, and artifact verification pass.

## Review Focus

- Pending Sites must not inflate critical/degraded counts.
- Pending Sites must carry stable `pendingRestart` and `activationStatus` fields.
- Active Sites must preserve existing trend, acknowledgement, and database fallback behavior.
- Browser and Desktop must explain pending state in Thai rather than showing a generic critical label.

### Task 1: Add failing contracts

**Files:**
- Create: `tests/AegisWAF.Tests/SiteHealthSummaryRuntimeTruthContractTests.cs`

- [ ] Add Gateway, Browser, Desktop, and DTO assertions for pending metadata and exclusion from alert counts.
- [ ] Run the focused class and confirm failure because the summary endpoint and clients do not yet expose the complete contract.

### Task 2: Implement Gateway and client contract

**Files:**
- Modify: `src/AegisWAF.Gateway/GatewayApp.cs`
- Modify: `src/AegisWAF.Client/Models.cs`

- [ ] Inject `SiteRegistry`, compute pending IDs with `SiteRuntimeState.IsPendingRestart`, filter alert counts, and project pending fields/count.
- [ ] Add optional DTO fields for pending summary items and aggregate count.
- [ ] Re-run focused tests and confirm green.

### Task 3: Implement Browser/Desktop presentation

**Files:**
- Modify: `src/AegisWAF.Gateway/wwwroot/assets/app.js`
- Modify: `src/AegisWAF.Desktop/Views/SitesView.xaml.cs`

- [ ] Include pending count in summary metadata/copy and Desktop summary text.
- [ ] Run JavaScript syntax and focused client contracts.

### Task 4: Verify, publish, and record

**Files:**
- Create: `artifacts/module-verification/module180-multisite-health-summary-runtime-truth.json`
- Modify: `handoff.md`
- Modify: `tests/AegisWAF.Tests/HandoffLatestStateContractTests.cs`

- [ ] Run the full targeted Multi-Site regression, Release build, `node --check`, and `git diff --check`.
- [ ] Publish and verify manifest/hash/PE/AuthentiCode status.
- [ ] Record Module 180 `0% → 100%` and update the final handoff pointer.
- [ ] Commit implementation and evidence.
