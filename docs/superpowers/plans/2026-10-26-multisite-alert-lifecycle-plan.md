# Multi-site Alert Lifecycle and Cross-site Comparison Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add auditable, bounded alert acknowledgement and a cross-site health comparison to Multi-site.

**Architecture:** Persist acknowledgement state beside the site in PostgreSQL and reuse the pure trend calculator for all summary projections. Keep the API/client/UI contracts explicit and bounded; do not introduce external notification side effects.

**Tech Stack:** .NET 10, ASP.NET Minimal APIs, PostgreSQL/Npgsql, WPF, xUnit in-process runner.

**Spec:** `docs/superpowers/specs/2026-10-26-multisite-alert-lifecycle-design.md`

## Global Constraints

- Cooldown must be 60..86400 seconds.
- Only critical/degraded alerts are actionable.
- Raw history remains capped at 64 observations per site.
- Cross-site result is capped at 100 sites and contains no upstream address/credentials/reasons.
- Acknowledgement writes and audit writes must be atomic.

## Review Focus

- Repeated acknowledgement must update one row, not create duplicates.
- Acknowledging a changed/healthy state must not suppress a new alert.
- Viewer reads remain allowed while acknowledgement requires operator role.
- Empty/stale sites must be represented explicitly, not omitted silently.
- UI/API failures must preserve existing Site editing and health-check actions.

### Task 1: Pure lifecycle and bounded summary contracts

**Files:**
- Create: `src/AegisWAF.Gateway/Sites/SiteHealthAlert.cs`
- Create: `tests/AegisWAF.Tests/SiteHealthAlertTests.cs`

- [ ] Write failing tests for cooldown bounds, actionable states, and acknowledgement matching.
- [ ] Write failing tests for bounded summary ordering and critical/degraded counts.
- [ ] Run focused tests and observe the expected missing-type failures.
- [ ] Implement the pure contracts and run focused tests green.

### Task 2: PostgreSQL persistence and API

**Files:**
- Create: `database/016_site_health_alerts.sql`
- Modify: `src/AegisWAF.Gateway/Sites/SiteDatabaseStore.cs`
- Modify: `src/AegisWAF.Gateway/GatewayApp.cs`
- Modify: `src/AegisWAF.Client/Models.cs`
- Modify: `src/AegisWAF.Client/ApiClient.cs`
- Modify: `tests/AegisWAF.Tests/SiteDatabaseCrudTests.cs`

- [ ] Add red API tests for repeated acknowledgement, state mismatch, summary, and role boundary.
- [ ] Apply the migration and implement transactional upsert + audit.
- [ ] Add trend acknowledgement fields and bounded summary endpoint.
- [ ] Run PostgreSQL-focused tests green.

### Task 3: Desktop contract and verification

**Files:**
- Modify: `src/AegisWAF.Desktop/Views/SitesView.xaml`
- Modify: `src/AegisWAF.Desktop/Views/SitesView.xaml.cs`
- Modify: `tests/AegisWAF.Tests/UiContractTests.cs`
- Create: `artifacts/module-verification/<timestamp>-multisite-module16-alert-lifecycle.json`
- Modify: `handoff.md`

- [ ] Add a bounded summary label and operator-only acknowledge action with stable fallback states.
- [ ] Run Desktop contract, solution build, focused PostgreSQL tests, and full regression.
- [ ] Record exact before/after percentages and evidence in `handoff.md`.
