# Handoff authoritative pointer integrity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ensure the latest `handoff.md` continuation pointer is physically the final non-whitespace content and cannot silently regress to an older pointer.

**Architecture:** Preserve all historical records. Strengthen the existing `HandoffLatestStateContractTests` to identify the last pointer and require the expected latest marker to be that last pointer and the EOF suffix. Append a Module 178 progress record and final pointer at EOF.

**Tech Stack:** Markdown, C# xUnit contract tests, the repository in-process test runner.

**Spec:** `docs/superpowers/specs/2026-10-30-handoff-authoritative-pointer-integrity-design.md`

## Global Constraints

- Keep historical `handoff.md` records intact.
- Report Module 178 as `0% → 100%` only after the contract test and EOF inspection pass.
- Do not change Gateway/Desktop runtime behavior in this module.

## Review Focus

- An older pointer appears after the latest marker — test that the latest pointer is the final non-whitespace suffix.
- The latest marker exists but does not carry Module 177 and Module 178 progress — test both progress records.
- The latest artifact hash is stale — test the current Module 177 artifact hash in the final pointer.
- CRLF/trailing whitespace changes the perceived EOF — use `TrimEnd()` before the suffix assertion.

### Task 1: Strengthen the handoff contract

**Files:**
- Modify: `tests/AegisWAF.Tests/HandoffLatestStateContractTests.cs`

- [ ] Add a failing assertion that the Module 178 pointer marker is the last `Current handoff pointer` marker and that `handoff.TrimEnd()` ends with the pointer text.
- [ ] Run `Invoke-InProcClassTests.ps1 -Classes HandoffLatestStateContractTests` and confirm failure because the current file still ends with the Module 176 pointer.
- [ ] Keep existing progress and artifact assertions.

### Task 2: Repair the handoff tail

**Files:**
- Modify: `handoff.md`

- [ ] Append Module 178 progress, the corrected latest artifact pointer, and a final `after Module 178` pointer at EOF without deleting historical records.
- [ ] Re-run the contract and confirm it passes.

### Task 3: Verify and record

**Files:**
- Create: `artifacts/module-verification/module178-handoff-authoritative-pointer-integrity.json`

- [ ] Run the focused contract, `git diff --check`, and inspect the final 20 lines.
- [ ] Record `0% → 100%`, evidence, and boundaries in the JSON and `handoff.md`.
- [ ] Commit the plan, spec, test, evidence, and handoff update.
