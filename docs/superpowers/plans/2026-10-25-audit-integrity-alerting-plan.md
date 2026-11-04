# Plan: Audit Integrity Alerting

> **For implementer:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Emit a deduplicated structured security event when the audit-chain integrity endpoint detects tampering.

**Architecture:** Keep audit rows immutable during verification. Add a singleton state object that deduplicates alert ids, inject it with `SecurityEvents` into the existing endpoint, and preserve the current JSON response and authorization.

**Tech stack:** ASP.NET Core minimal APIs, C#, xUnit, existing `SecurityEvents` queue and structured logger.

### Task 1: Add failing alert-state tests

**Files:** `tests/AegisWAF.Tests/AuditIntegrityAlertTests.cs`

- Test that the first invalid id is accepted for alerting.
- Test that the same invalid id is suppressed.
- Test that a different invalid id is accepted.
- Run the focused test before implementation and record the expected compile failure in the progress ledger.

### Task 2: Implement the alert state and endpoint emission

**Files:** `src/AegisWAF.Gateway/AuditIntegrityAlertState.cs`, `src/AegisWAF.Gateway/GatewayApp.cs`

- Add a thread-safe singleton holding alerted invalid ids.
- Register it in dependency injection.
- Inject `SecurityEvents` and the state into `/api/audit/integrity`.
- On invalid results, emit `Audit Integrity Failure` with `firstInvalidId` and request id, once per id.
- Do not write to `waf_audit` or change valid results and database-error behavior.

### Task 3: Document and register the module

**Files:** `docs/PRODUCTION_READINESS.md`, `docs/SECURITY.md`, `tools/module-verification/modules.json`, `handoff.md`, `.superpowers/sdd/2026-10-25-audit-integrity-alerting-plan/progress.md`

- Move the audit-chain SIEM alert item from remaining gate to implemented control, while retaining independent operational validation as pending.
- Add module 11 with build, focused tests, structured logging contract, and docs contract gates.
- Record before/after percentages and evidence.

### Task 4: Verify and commit

- Run focused tests, full test suite, gateway/Desktop builds, and all module verification.
- Run `git diff --check` and inspect the final diff.
- Commit only scoped changes with message `feat: alert on audit integrity failures`.
- Refresh `handoff.md` with the final implementation commit and evidence path.
