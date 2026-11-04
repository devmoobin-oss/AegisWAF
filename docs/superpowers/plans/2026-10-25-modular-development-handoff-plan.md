# Modular Development Handoff Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a reproducible module-verification workflow and use it to advance AegisWAF module-by-module while recording evidence-based before/after percentages in `handoff.md`.

**Architecture:** A PowerShell verifier owns the deterministic command set and emits a JSON evidence report. `handoff.md` remains the human-readable ledger and is updated after each completed module with the measured before/after value, evidence, and remaining risks. Existing product modules remain unchanged except for defects found by their gates.

**Tech Stack:** PowerShell 7-compatible syntax, .NET 10 SDK, xUnit, WPF, Markdown, JSON.

**Spec:** `docs/superpowers/specs/2026-10-25-modular-development-handoff-design.md`

## Global Constraints

- Read `handoff.md` and `git status` before each development round.
- Preserve existing user changes in the current worktree; do not reset or delete unrelated files.
- Use `dotnet ... --disable-build-servers -m:1` for deterministic local verification.
- Do not report a module above 98% without fresh required-gate evidence.
- Security documentation must distinguish implementation readiness from independent security assurance.

## Review Focus

- A failed or timed-out command must remain unverified and must not inflate a module percentage; test with a deliberately failing command fixture in the verifier test.
- A command containing spaces or quoted arguments must execute as one command; test with a PowerShell fixture command.
- A stale module entry in `handoff.md` must not be silently rewritten as complete; test missing/unknown module handling.
- Existing uncommitted source changes must remain untouched by the verifier; test that only the intended ledger/report files change.
- WPF build failures must be attributed to the desktop module and not incorrectly mark Gateway/Tests green; verify per-project command isolation.

---

### Task 1: Define module manifest and verifier contract

**Files:**
- Create: `tools/module-verification/modules.json`
- Create: `tools/module-verification/verify-modules.ps1`
- Test: `tools/module-verification/verify-modules.tests.ps1`

**Interfaces:**
- Consumes: repository root, module name filter, optional `-SkipTests`, and `modules.json`.
- Produces: `artifacts/module-verification/<timestamp>.json` containing module, gate, command, exit code, duration, and evidence text; optional handoff update mode.

- [ ] **Step 1: Write failing verifier tests**

  Add plain PowerShell assertions for: successful command, failing command, quoted command arguments, unknown module, and bounded timeout result. Tests use a temporary fixture directory and do not touch the repository ledger.

- [ ] **Step 2: Run the verifier tests and verify they fail for the missing contract**

  Run: `powershell -NoProfile -File tools/module-verification/verify-modules.tests.ps1`

  Expected: FAIL because the verifier script and manifest do not exist.

- [ ] **Step 3: Implement the manifest and verifier**

  Define the six module IDs and their weighted gates in JSON. Implement command execution with `System.Diagnostics.Process`, timeout cancellation, captured stdout/stderr, exit-code classification, and JSON report output. Default mode is read-only; `-UpdateHandoff` is required before changing `handoff.md`.

- [ ] **Step 4: Run the verifier tests and verify they pass**

  Run: `powershell -NoProfile -File tools/module-verification/verify-modules.tests.ps1`

  Expected: PASS with all fixture assertions passing.

- [ ] **Step 5: Commit**

  ```powershell
  git add tools/module-verification
  git commit -m "feat: add modular verification manifest"
  ```

### Task 2: Add evidence-based handoff update logic

**Files:**
- Modify: `tools/module-verification/verify-modules.ps1`
- Modify: `tools/module-verification/verify-modules.tests.ps1`
- Modify: `handoff.md`

**Interfaces:**
- Consumes: verifier JSON results and existing `handoff.md`.
- Produces: an append-only verification entry and a module ledger row update with before/after percentages.

- [ ] **Step 1: Write failing handoff-update tests**

  Assert that update mode records the prior percentage, new measured percentage, exact evidence report path, and unresolved gates; assert that a failed gate cannot produce a value above 98%.

- [ ] **Step 2: Run the tests and verify they fail**

  Run: `powershell -NoProfile -File tools/module-verification/verify-modules.tests.ps1`

  Expected: FAIL because update mode and percentage calculation are not implemented.

- [ ] **Step 3: Implement fixed weighted percentage calculation and append-only ledger updates**

  Read the current module row before writing, calculate passed gate weights, keep failed/timeout gates visible, and add a dated entry under `## Verification history`. Never overwrite unrelated sections or source files.

- [ ] **Step 4: Run the tests and verify they pass**

  Run: `powershell -NoProfile -File tools/module-verification/verify-modules.tests.ps1`

  Expected: PASS; a simulated failed gate remains below the target and the fixture handoff is updated only in the expected sections.

- [ ] **Step 5: Commit**

  ```powershell
  git add tools/module-verification handoff.md
  git commit -m "feat: record module verification handoffs"
  ```

### Task 3: Stabilize Desktop UI module using the verifier

**Files:**
- Modify: `src/AegisWAF.Desktop/App.xaml`
- Modify: `tests/AegisWAF.Tests/UiContractTests.cs`
- Modify: `handoff.md`

**Interfaces:**
- Consumes: Desktop project and UI contract build gates.
- Produces: a Desktop module report with the missing-resource regression covered and the current build/runtime limitations recorded.

- [ ] **Step 1: Run the failing Desktop build gate**

  Run: `dotnet build src/AegisWAF.Desktop/AegisWAF.Desktop.csproj --no-restore --disable-build-servers -m:1 --verbosity minimal`

  Expected before the fix: FAIL at `App.xaml` with `MC3074` for missing `RuleInkConverter`.

- [ ] **Step 2: Implement the minimal root-cause fix and contract test**

  Remove stale converter resource declarations that have no corresponding source type, and keep a source/XAML contract test so future deleted converter types fail before packaging.

- [ ] **Step 3: Run the Desktop build gate**

  Run: `dotnet build src/AegisWAF.Desktop/AegisWAF.Desktop.csproj --no-restore --disable-build-servers -m:1 --verbosity minimal`

  Expected: PASS with 0 warnings and 0 errors.

- [ ] **Step 4: Run the available Desktop/UI tests and record environment limits**

  Run: `dotnet build tests/AegisWAF.Tests/AegisWAF.Tests.csproj --no-restore --disable-build-servers -m:1 --verbosity minimal` and the filtered xUnit command. If testhost cannot connect, record it as unverified rather than passing it.

- [ ] **Step 5: Update `handoff.md` with before/after evidence**

  Record the actual measured values, the Desktop build output, the contract test status, and any unverified WPF runtime gate.

### Task 4: Run the six-module verification pass and close remaining gaps

**Files:**
- Modify: `handoff.md`
- Modify: module source/tests only when a failing gate has a reproducible root cause
- Create: `artifacts/module-verification/*.json`

**Interfaces:**
- Consumes: the verifier manifest, current source, and Task 3 evidence.
- Produces: one evidence report per module and a completion audit showing which modules exceed 98% and which remain below due to real gaps.

- [ ] **Step 1: Run each module's read-only verification command set**

  Run: `powershell -NoProfile -File tools/module-verification/verify-modules.ps1 -All`

- [ ] **Step 2: Investigate each failed gate systematically**

  For every failure, reproduce it, add a regression test, make one root-cause fix, and rerun the owning module only before rerunning the full suite.

- [ ] **Step 3: Run the complete available verification set**

  Run the six module verifier, each project build with `--disable-build-servers -m:1`, the full xUnit command with a bounded timeout, and the safe load-test harness where its prerequisites are available.

- [ ] **Step 4: Update `handoff.md` after every completed module**

  Each entry must include before %, after %, exact commands, result counts, unresolved risks, and next module. Do not round an incomplete module above 98%.

- [ ] **Step 5: Completion audit**

  Re-read the objective and `handoff.md`; verify every module has evidence and a percentage over 98% before making a completion claim. If any module is below target, leave the goal active and report the exact blocker.
