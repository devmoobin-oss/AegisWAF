# Multi-Site health summary pending count scope Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Make the bounded pending Site count explicit to operators.

**Architecture:** Reuse the existing `countsScope` metadata in Browser and Desktop rendering. The API remains bounded and does not perform a second global count query.

**Tech Stack:** Browser JavaScript, WPF C#, xUnit contract tests, self-contained .NET publish.

**Spec:** `docs/superpowers/specs/2026-10-30-multisite-health-summary-pending-scope-design.md`

## Global Constraints

- Keep active and pending runtime behavior unchanged.
- Do not claim the pending count is global when Summary is truncated.
- Report Module 182 as `0% → 100%` only after red/green tests, regression, build, and artifact verification pass.

### Task 1: Add failing presentation contracts

**Files:**
- Create: `tests/AegisWAF.Tests/SiteHealthSummaryPendingScopeContractTests.cs`

- [x] Require Browser to explain page-scoped pending count.
- [x] Require Desktop to explain page-scoped pending count.
- [x] Confirm the new contracts fail before implementation.

### Task 2: Clarify operator copy

**Files:**
- Modify: `src/AegisWAF.Gateway/wwwroot/assets/app.js`
- Modify: `src/AegisWAF.Desktop/Views/SitesView.xaml.cs`

- [x] Use `countsScope=returned-sites` to render “รายการนี้รอเปิดใช้”.
- [x] Preserve the existing global-scope wording when the summary is not truncated.

### Task 3: Verify and record

**Files:**
- Create: `artifacts/module-verification/module182-multisite-health-summary-pending-scope.json`
- Modify: `handoff.md`

- [x] Run focused and full targeted Multi-Site regression.
- [x] Run Release build, JavaScript syntax, diff, and publish-manifest checks.
- [x] Record Module 182 `0% → 100%` and update the final handoff pointer.
