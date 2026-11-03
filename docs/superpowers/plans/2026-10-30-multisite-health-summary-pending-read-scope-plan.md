# Multi-Site health summary pending read scope Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Avoid reading persisted health data for Sites whose routes are not active in the Gateway runtime.

**Architecture:** Derive `activeSiteIds` after runtime truth classification. Use that bounded set for PostgreSQL history and acknowledgement reads, preserve pending rows in the response with empty trend input, and exclude pending in-memory observations from active retention metadata.

**Tech Stack:** ASP.NET Minimal APIs, C# records/xUnit contract tests, PostgreSQL-backed Site store, self-contained .NET publish.

**Spec:** `docs/superpowers/specs/2026-10-30-multisite-health-summary-pending-read-scope-design.md`

## Global Constraints

- Pending Sites remain visible and explicitly marked `pending-restart`.
- Active Site behavior and database fallback remain unchanged.
- Report Module 181 as `0% → 100%` only after red/green tests, regression, build, and artifact verification pass.

### Task 1: Add failing contracts

**Files:**
- Create: `tests/AegisWAF.Tests/SiteHealthSummaryPendingReadScopeContractTests.cs`

- [x] Assert history reads use `activeSiteIds`.
- [x] Assert acknowledgement reads use `activeSiteIds`.
- [x] Confirm the new contracts fail against the pre-change endpoint.

### Task 2: Scope runtime reads

**Files:**
- Modify: `src/AegisWAF.Gateway/GatewayApp.cs`

- [x] Derive `activeSiteIds` from pending runtime classification.
- [x] Skip both database reads when the current page contains no active Sites.
- [x] Preserve pending response rows while avoiding their history merge and retention accounting.

### Task 3: Verify and record

**Files:**
- Create: `artifacts/module-verification/module181-multisite-health-summary-pending-read-scope.json`
- Modify: `handoff.md`

- [x] Run focused and full targeted Multi-Site regression.
- [x] Run Release build, JavaScript syntax, diff, and publish-manifest checks.
- [x] Record Module 181 `0% → 100%` and update the final handoff pointer.
