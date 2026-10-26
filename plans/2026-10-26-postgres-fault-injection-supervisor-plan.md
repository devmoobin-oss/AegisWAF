# PostgreSQL Fault-Injection Supervisor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add an explicit, bounded, opt-in supervisor that verifies PostgreSQL outage/reconnect convergence for a real Embedded Gateway without xUnit teardown coupling.

**Architecture:** A small net10 console runner creates a disposable PostgreSQL database, starts `EmbeddedGateway` on a loopback port, and waits for a sentinel. A PowerShell supervisor owns PostgreSQL stop/start, polls `/ready`, writes redacted JSON evidence, signals the runner to stop, and verifies final cleanup.

**Tech Stack:** .NET 10, ASP.NET Core EmbeddedGateway, Npgsql, PowerShell, PostgreSQL `pg_ctl`.

**Spec:** `docs/superpowers/specs/2026-10-26-postgres-fault-injection-supervisor-design.md`

## Global Constraints

- Normal test suites must never stop or start PostgreSQL.
- The run requires `AEGISWAF_ENABLE_PG_FAULT_INJECTION=1` and `-ManagePostgres`.
- Database name is generated and disposable; credentials never enter evidence.
- Every wait is bounded and cleanup runs in `finally`.
- Do not claim 100% without a real run that observes both degraded and recovered states.

## Review Focus

- PostgreSQL is already stopped before launch: the supervisor must not accidentally claim it owns an unavailable service.
- Runner startup fails: the supervisor must capture the failure and still clean up the child process/database.
- Stop command returns but the port remains open: outage polling must wait for actual health degradation, not command exit alone.
- Restart does not converge: the run must time out with evidence and never report success.
- Ctrl+C/exception during recovery: cleanup must be bounded and restore the pre-run database state.

### Task 1: Contract and runner skeleton

**Files:**
- Create: `tools/fault-injection/SiteRuntimeFaultInjectionRunner.csproj`
- Create: `tools/fault-injection/Program.cs`
- Test: `tests/AegisWAF.Tests/SiteRuntimeFaultInjectionSupervisorContractTests.cs`

- [ ] Write contract tests for opt-in, explicit lifecycle flag, bounded sentinel, disposable database, and `/ready` runner metadata.
- [ ] Run the targeted contract test and observe the expected failure because the runner does not exist.
- [ ] Implement the minimal runner that creates a unique database, starts `EmbeddedGateway`, writes redacted metadata, and exits only after a stop sentinel.
- [ ] Run the contract test again and verify it passes.

### Task 2: PowerShell supervisor

**Files:**
- Create: `tools/fault-injection/Invoke-SiteRuntimeFaultInjection.ps1`
- Create: `tools/fault-injection/test-fault-injection-supervisor.ps1`
- Modify: `tools/fault-injection/README.md`

- [ ] Add snapshot/contract self-test for missing opt-in and cleanup path.
- [ ] Implement bounded `pg_ctl` start/stop, runner process ownership, `/ready` polling, redacted evidence, and `finally` cleanup.
- [ ] Verify self-test passes without touching PostgreSQL.

### Task 3: Real PostgreSQL outage/reconnect run

**Files:**
- Create: `artifacts/module-verification/<timestamp>-module49-postgres-fault-injection-final.json`
- Modify: `handoff.md`

- [ ] Start PostgreSQL only under explicit supervisor ownership.
- [ ] Run the opt-in supervisor and observe ready → degraded → ready.
- [ ] Verify runner exit, disposable database removal, and PostgreSQL final state.
- [ ] Run focused regression, solution build, `git diff --check`, and record Before/After/Target.

