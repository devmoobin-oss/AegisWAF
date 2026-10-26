# Multi-site Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** ยกระดับ Multi-site ให้ทุก lifecycle path ใช้ validation/persistence/runtime invariant เดียวกัน พร้อมหลักฐานทดสอบครบทุกโมดูล

**Architecture:** แยก typed validation ออกจาก configuration binding แล้วใช้เป็น shared gate สำหรับ API writes และ DB snapshots เพิ่ม schema สำหรับ policy ที่หายไป ใช้ transaction กับ uniqueness/audit และส่ง immutable snapshot เดียวผ่าน startup dependency graph

**Tech Stack:** .NET 10, ASP.NET Core, YARP, PostgreSQL/Npgsql, xUnit, existing in-process runner

**Spec:** `docs/superpowers/specs/2026-10-26-multisite-hardening-design.md`

## Global Constraints

- Production mode requires HTTPS upstreams and explicit egress allowlists.
- No credentials may be accepted in upstream URI.
- Legacy configuration remains supported until migration is complete.
- Every completed module must update `handoff.md` with before/after percentage and evidence.
- Do not claim 100% when any gate is failed or unverified.

## Review Focus

- DB-loaded unsafe Site must be rejected before YARP configuration is built.
- Concurrent writes for the same active hostname must have one deterministic winner.
- Custom health/rate-limit policy must survive persistence and restart.
- Corrupt database arrays must fail closed with an actionable error.
- Cached health status must not be presented as a fresh probe.

### Module 1: Snapshot validation parity and secure mutation

**Files:**
- Modify: `src/AegisWAF.Gateway/Sites/SiteConfiguration.cs`
- Modify: `src/AegisWAF.Gateway/Sites/SiteConfigurationEditor.cs`
- Modify: `src/AegisWAF.Gateway/GatewayApp.cs`
- Test: `tests/AegisWAF.Tests/SiteConfigurationTests.cs`
- Test: `tests/AegisWAF.Tests/SiteConfigurationEditorTests.cs`

- [ ] Write failing tests for unsafe typed snapshots, undefined policy, duplicate active hosts, and production API mutation.
- [ ] Run focused tests and confirm expected failures.
- [ ] Implement typed `GatewaySite` validation shared by configuration, DB snapshots, and API writes.
- [ ] Make production startup reject invalid DB snapshot before route generation.
- [ ] Run focused tests and solution build.
- [ ] Update `handoff.md` with before/after and evidence.

### Module 2: Persistence completeness and corrupt-row handling

**Files:**
- Create: `database/011_site_policy_details.sql`
- Modify: `src/AegisWAF.Gateway/Sites/SiteDatabaseStore.cs`
- Test: `tests/AegisWAF.Tests/SiteDatabaseCrudTests.cs`

- [ ] Write failing round-trip tests for health/rate-limit and malformed row handling.
- [ ] Add migration and read/write parameters for all per-site policies.
- [ ] Validate row shape before constructing `GatewaySite`.
- [ ] Add compatibility/backfill behavior for existing rows.
- [ ] Run database-backed tests when PostgreSQL is available; otherwise record an explicit unverified gate.
- [ ] Update `handoff.md`.

### Module 3: Hostname uniqueness and atomic audit trail

**Files:**
- Create: migration for normalized hostname uniqueness or equivalent constraint
- Modify: `src/AegisWAF.Gateway/Sites/SiteDatabaseStore.cs`
- Modify: audit integration in `src/AegisWAF.Gateway/GatewayApp.cs`
- Test: database CRUD and audit tests

- [ ] Write failing concurrent create and audit assertions.
- [ ] Implement transaction-safe active hostname reservation.
- [ ] Record create/update/delete audit events atomically without secrets.
- [ ] Verify concurrency and rollback behavior.
- [ ] Update `handoff.md`.

### Module 4: Single immutable startup snapshot and operational errors

**Files:**
- Modify: `src/AegisWAF.Gateway/GatewayApp.cs`
- Modify: `src/AegisWAF.Gateway/Sites/SiteDatabaseStore.cs`
- Test: startup and error contract tests

- [ ] Add test proving route and registry use the same snapshot instance/version.
- [ ] Load snapshot once and inject it to all consumers.
- [ ] Return structured 503/trace IDs for database availability failures.
- [ ] Update `handoff.md`.

### Module 5: Health probe semantics and readiness

**Files:**
- Modify: `src/AegisWAF.Gateway/Sites/SiteRoutingHealthCheck.cs`
- Modify: `src/AegisWAF.Gateway/GatewayApp.cs`
- Modify: client health status contract
- Test: health and readiness tests

- [ ] Write failing tests distinguishing cached status from active probe.
- [ ] Implement bounded on-demand probe or rename endpoint to status.
- [ ] Expose last probe age and unknown-state semantics.
- [ ] Update `handoff.md`.

### Module 6: Multi-site UI/API completeness

**Files:**
- Modify: `src/AegisWAF.Client/Models.cs`
- Modify: `src/AegisWAF.Desktop/Views/SitesView.xaml`
- Modify: `src/AegisWAF.Desktop/Views/SitesView.xaml.cs`
- Test: UI/API contract tests

- [ ] Write contract tests for multiple upstreams and policy fields.
- [ ] Add advanced editor fields or explicitly label simple-profile limits.
- [ ] Verify validation errors are field-specific and safe.
- [ ] Update `handoff.md`.

### Module 7: Integration and production verification

**Files:**
- Modify: test harness and verification scripts as needed
- Create: module verification artifacts
- Modify: `handoff.md`

- [ ] Run full solution build with warnings as errors.
- [ ] Run focused and full in-process tests.
- [ ] Run PostgreSQL integration, restart, concurrency and failover tests.
- [ ] Review all module gates and only mark 100% with evidence.
- [ ] After all modules reach 100%, perform a second Multi-site opportunity audit.
