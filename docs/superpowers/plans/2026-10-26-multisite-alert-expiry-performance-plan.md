# Multi-site Alert Expiry and Summary Performance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Make acknowledgement expiry observable and prove the cross-site summary remains bounded.

**Architecture:** Reuse the indexed acknowledgement table and existing bounded history query. Cleanup is an explicit store operation invoked by the summary read path; no unbounded background worker is introduced.

**Tech Stack:** .NET 10, ASP.NET Minimal APIs, PostgreSQL/Npgsql, xUnit in-process runner.

**Spec:** `docs/superpowers/specs/2026-10-26-multisite-alert-expiry-performance-design.md`

## Global Constraints

- Maximum trend window: 64 observations/site.
- Maximum summary result: 100 sites.
- Cleanup deletes only acknowledgement rows whose expiry is at or before `now`.
- Raw health-history rows are never deleted by cleanup.

### Task 1: Red integration contract

- [ ] Insert an expired acknowledgement in PostgreSQL and request summary with oversized bounds.
- [ ] Assert the summary reports cleanup and returns capped bounds.
- [ ] Run the test and observe the missing cleanup field/failure.

### Task 2: Minimal implementation

- [ ] Add `CleanupExpiredHealthAlertAcknowledgementsAsync` to `SiteDatabaseStore`.
- [ ] Invoke it from the bounded summary endpoint and expose the removed count.
- [ ] Run focused tests green.

### Task 3: Verification and handoff

- [ ] Run build, runner build, focused suite, and full regression.
- [ ] Record Module 17 evidence and next opportunity in `handoff.md`.
