# Module 98 — Security Action Dialogs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Replace native browser dialogs in Custom Rules and IP Policy with one accessible app-owned dialog and consistent API error recovery.

**Architecture:** Extend the existing vanilla browser console in `src/AegisWAF.Gateway/wwwroot/assets/app.js` with a single `showAppDialog(options)` primitive. Keep destructive requests pessimistic, keep failures in context, and reuse `readApiError` plus the existing status region. Add source-contract tests because the browser workflow is generated inside one legacy script.

**Tech Stack:** Vanilla JavaScript, HTML/CSS already shipped by AegisWAF Gateway, xUnit in-process source-contract tests, Node syntax check, .NET Release build.

**Spec:** `docs/superpowers/specs/2026-10-27-multisite-security-dialogs-design.md`

## Global Constraints

- No new runtime dependency or API contract change.
- Destructive dialog must use `role="alertdialog"`, `aria-modal`, accessible name/description, Cancel-first focus, Escape, Tab trap, and focus restoration.
- DELETE mutation remains open/pending until server response; duplicate confirmation is prevented.
- User-facing errors use bounded `readApiError` output and never raw exception/trace data.
- Existing browser-console visual language and Thai copy remain authoritative.

## Review Focus

- Keyboard users cannot escape the dialog trap or lose focus after close — covered by dialog source contract assertions.
- A double-click can issue duplicate DELETE/PUT requests — covered by pending/disabled contract assertions and relevant regression.
- Server failure loses the entered data or hides the recovery action — covered by source assertions for status/error paths.
- HTML in a rule/IP identifier is rendered unsafely in dialog copy — covered by `escapeHtml` contract use.
- Legacy native `confirm`/`alert` remains in the touched workflows — covered by scoped source absence assertions.

---

### Task 1: Add the failing browser security-dialog contract

**Files:**
- Create: `tests/AegisWAF.Tests/BrowserSecurityActionDialogContractTests.cs`
- Read: `src/AegisWAF.Gateway/wwwroot/assets/app.js`

**Interfaces:**
- Consumes: the generated `rules` and `ips` workflow source slices.
- Produces: one focused xUnit contract named `Browser_security_actions_use_the_shared_accessible_dialog`.

- [ ] Write assertions for `showAppDialog`, required ARIA/focus hooks, bounded error handling, disabled pending confirmation, and absence of native `confirm(`/`alert(` in the scoped rules/IP workflow.
- [ ] Run the focused test and record the expected RED failure because the shared primitive and scoped absence contract do not yet exist.

### Task 2: Implement the shared dialog and migrate Custom Rules

**Files:**
- Modify: `src/AegisWAF.Gateway/wwwroot/assets/app.js` in the shared browser helpers and `active==='rules'` branch.

**Interfaces:**
- Consumes: `escapeHtml`, `readApiError`, `status`, and existing `authFetch`.
- Produces: `showAppDialog({title, description, confirmLabel, cancelLabel, danger, onConfirm}) -> Promise<boolean>` with focus-safe app-owned modal behavior.

- [ ] Implement the smallest dialog primitive satisfying the contract, including pending state that keeps the dialog open until `onConfirm` resolves.
- [ ] Replace Custom Rule delete `confirm()` and all touched `alert()` error paths with dialog/status handling; preserve form values on failure.
- [ ] Run the focused test and confirm the Custom Rules portion is GREEN.

### Task 3: Migrate IP Policy and verify the complete regression

**Files:**
- Modify: `src/AegisWAF.Gateway/wwwroot/assets/app.js` in the `active==='ips'` branch.
- Modify: `tests/AegisWAF.Tests/BrowserSecurityActionDialogContractTests.cs` if needed for exact scoped assertions.

**Interfaces:**
- Consumes: `showAppDialog` from Task 2 and existing `readApiError`/status behavior.
- Produces: consistent delete/save failure recovery for IP Policy.

- [ ] Replace IP Policy delete `confirm()` and save/delete `alert()` paths; keep request pending state and restore/refresh authoritative data on failure.
- [ ] Run focused contract, relevant Multi-Site/security regression, Node syntax, Release solution build, `git diff --check`, and Impeccable detector.
- [ ] Record evidence JSON, 0% → 100% progress, source commit, and verification boundaries in `handoff.md`; commit the implementation and handoff together.
