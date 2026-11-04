# Multi-site Health Trend and Operator Alerting Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add a bounded PostgreSQL-backed health trend projection and deterministic operator alert state to Multi-site.

**Architecture:** Keep the existing persisted observation table and add a pure calculator for trend semantics. Expose the calculator through a gateway endpoint and typed client contract without adding notification side effects or unbounded state.

**Tech Stack:** .NET 10, ASP.NET Minimal APIs, PostgreSQL/Npgsql, xUnit in-process runner.

**Spec:** `docs/superpowers/specs/2026-10-26-multisite-health-trend-design.md`

## Global Constraints

- Retention remains at most 64 observations per site.
- API window remains capped at 64 for trend calculation and 100 for raw history.
- Trend output must not expose upstream address, credentials, or raw exception data.
- PostgreSQL is the source of truth for persisted observations.

## Review Focus

- Empty history must return `no-data`, not a false healthy signal.
- Newest-first observations must count consecutive unhealthy values from the newest item only.
- Mixed `healthy`/`unknown`/`unhealthy` values must produce stable counts and percentages.
- Oversized or invalid windows must remain bounded.
- API output must not leak address or credentials.

### Task 1: Pure trend calculator

**Files:**
- Create: `src/AegisWAF.Gateway/Sites/SiteHealthTrend.cs`
- Create: `tests/AegisWAF.Tests/SiteHealthTrendTests.cs`

- [ ] Write tests for empty history, mixed counts/percentage, and three newest consecutive unhealthy observations.
- [ ] Run the focused trend tests and verify they fail because the calculator is absent.
- [ ] Implement `SiteHealthTrendCalculator.Calculate(IReadOnlyList<SiteHealthObservation>, int)` with bounded window, newest-first consecutive count, and the four alert states.
- [ ] Run the focused tests and verify they pass.

### Task 2: Gateway and client contract

**Files:**
- Modify: `src/AegisWAF.Gateway/GatewayApp.cs`
- Modify: `src/AegisWAF.Client/Models.cs`
- Modify: `src/AegisWAF.Client/ApiClient.cs`
- Modify: `tests/AegisWAF.Tests/SiteDatabaseCrudTests.cs`

- [ ] Add an API contract test that persists three unhealthy observations and expects `critical`.
- [ ] Run the contract test before implementation and verify it fails with a missing route.
- [ ] Add the typed DTO/client method and bounded `/health/trend` endpoint using PostgreSQL history plus the pure calculator.
- [ ] Run the focused API tests and verify the endpoint passes and remains secret-safe.

### Task 3: Verification and handoff

**Files:**
- Create: `artifacts/module-verification/<timestamp>-multisite-module14-health-trend.json`
- Modify: `handoff.md`

- [ ] Build solution and runner with warnings treated as errors.
- [ ] Run focused trend/API/PostgreSQL tests.
- [ ] Run full regression and record exact counts.
- [ ] Record Module 14 before/after percentages and evidence in `handoff.md`.
