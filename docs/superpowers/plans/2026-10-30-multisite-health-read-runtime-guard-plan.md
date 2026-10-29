# Multi-Site health read runtime guard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prevent health history and trend endpoints from presenting persisted Alert data for a Site whose route is still pending runtime activation.

**Architecture:** Reuse `SiteRuntimeState.IsPendingRestart` after Site resolution in both read endpoints. Return a stable pending envelope before any history read or Alert calculation; preserve existing active-site database fallback and budgets.

**Tech Stack:** ASP.NET Minimal APIs, C#, xUnit contract tests, browser JavaScript, self-contained .NET publish.

**Spec:** `docs/superpowers/specs/2026-10-30-multisite-health-read-runtime-guard-design.md`

## Global Constraints

- Do not mutate, delete, or auto-activate Site configuration.
- Keep database fallback for active runtime Sites.
- Report Module 179 as `0% → 100%` only after red/green tests, regression, build, and artifact verification pass.

## Review Focus

- Pending restart must short-circuit before process-memory or PostgreSQL history reads.
- History and trend must expose an explicit pending state instead of ambiguous empty/critical data.
- Active Sites must retain existing database-unavailable fallback behavior.
- Existing role, CSRF, timeout, and write boundaries must remain unchanged.

### Task 1: Add failing read-guard contracts

**Files:**
- Create: `tests/AegisWAF.Tests/SiteHealthReadRuntimeGuardContractTests.cs`

- [ ] Add isolated history and trend endpoint tests requiring `SiteRuntimeState.IsPendingRestart(site, active)` and a pending response envelope before history merge/calculation.
- [ ] Run the focused class and confirm failure because both endpoints currently read history without a runtime guard.

### Task 2: Implement the minimal endpoint guard

**Files:**
- Modify: `src/AegisWAF.Gateway/GatewayApp.cs`

- [ ] Inject `SiteRegistry` into both endpoints and resolve the active snapshot.
- [ ] Return pending envelopes before database history reads and trend Alert calculation.
- [ ] Re-run focused contracts and confirm green.

### Task 3: Verify, publish, and record

**Files:**
- Create: `artifacts/module-verification/module179-multisite-health-read-runtime-guard.json`
- Modify: `handoff.md`
- Modify: `tests/AegisWAF.Tests/HandoffLatestStateContractTests.cs`

- [ ] Run the Multi-Site regression, `node --check`, Release build, and `git diff --check`.
- [ ] Publish the completed-modules bundle, verify manifest/hash/PE/AuthentiCode status.
- [ ] Record Module 179 `0% → 100%` and make its pointer the final handoff pointer.
- [ ] Commit implementation and evidence.
