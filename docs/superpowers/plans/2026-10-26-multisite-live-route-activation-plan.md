# Multi-site Live Route Activation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Activate Multi-site add/update/delete mutations in the running Gateway without requiring a restart.

**Architecture:** A process-local `SiteRuntimeConfigurationProvider` supplies the complete generated `ReverseProxy` section to YARP's existing configuration loader and raises reload notifications. `SiteRuntimeCoordinator` validates a new immutable snapshot, replaces the provider data, and publishes the matching `SiteRegistry` snapshot under one lock. Mutation endpoints persist first, then activate the candidate snapshot and report `active=true`.

**Tech Stack:** .NET 10, ASP.NET Core, YARP 2.3, PostgreSQL, WPF, xUnit

**Spec:** `docs/superpowers/specs/2026-10-26-multisite-live-route-activation-design.md`

## Global Constraints

- PostgreSQL remains the source of truth for Site persistence.
- Existing validation, row-version/ETag, RBAC, audit and hostname uniqueness behavior must remain enforced.
- No polling, process restart, schema migration or cross-node propagation is introduced.
- A failed activation must not publish a partial in-memory snapshot.
- The Desktop must not instruct operators to restart after a successful mutation.

## Review Focus

- Add/update/delete while requests are in flight: old requests finish safely and new requests use the current snapshot.
- Provider reload failure: old registry/routes remain active and no partial snapshot is published.
- Stale ETag: persistence and runtime snapshot must not be changed.
- Legacy/default Site transition: first live mutation must not create duplicate hostnames.
- Concurrent activation: snapshots and route configuration must not mix generations.

---

### Task 1: Add failing runtime-provider and registry tests

**Files:**
- Create: `tests/AegisWAF.Tests/SiteRuntimeActivationTests.cs`
- Modify: `tests/AegisWAF.Tests/SiteManagerContractTests.cs`

**Interfaces:**
- Produces tests for `SiteRuntimeConfigurationProvider`, `SiteRuntimeCoordinator`, and mutable atomic `SiteRegistry` snapshots.

- [ ] **Step 1: Write tests** for provider replacement, registry atomic publication, route removal, and provider failure preserving the previous snapshot.
- [ ] **Step 2: Add the live Desktop/API contract assertions** requiring `active=true`, no restart copy, and live activation wording.
- [ ] **Step 3: Run the focused tests** and confirm they fail because the provider/coordinator and live contracts do not exist.

### Task 2: Implement dynamic configuration and atomic runtime activation

**Files:**
- Create: `src/AegisWAF.Gateway/Sites/SiteRuntimeConfigurationProvider.cs`
- Create: `src/AegisWAF.Gateway/Sites/SiteRuntimeCoordinator.cs`
- Modify: `src/AegisWAF.Gateway/GatewayApp.cs`
- Modify: `src/AegisWAF.Gateway/Sites/SiteRegistry.cs`
- Modify: `src/AegisWAF.Gateway/Sites/SiteProxyConfiguration.cs`

**Interfaces:**
- `SiteRuntimeConfigurationProvider.Replace(IReadOnlyDictionary<string,string?>)` replaces the generated ReverseProxy data and triggers configuration reload.
- `SiteRuntimeCoordinator.Activate(SiteConfigurationSnapshot)` validates, replaces provider data, and publishes the registry snapshot atomically.
- `SiteRegistry.Current` returns the latest immutable snapshot; internal replacement is thread-safe.

- [ ] **Step 1: Implement the provider and registry primitives** with a lock and immutable data replacement.
- [ ] **Step 2: Register the provider before `LoadFromConfig`** so YARP observes runtime reload tokens while preserving existing route generation.
- [ ] **Step 3: Implement the coordinator** with validation-before-publish and previous-state preservation on failure.
- [ ] **Step 4: Run Task 1 focused tests** and confirm they pass.

### Task 3: Activate mutation endpoints and prove real live routing

**Files:**
- Modify: `src/AegisWAF.Gateway/GatewayApp.cs`
- Modify: `tests/AegisWAF.Tests/EmbeddedGatewayTests.cs`
- Modify: `src/AegisWAF.Client/Models.cs`

- [ ] **Step 1: Add a failing integration test** that starts the Gateway, adds a new Site through the authenticated API, immediately sends a request with the new Host header, edits/deletes it, and verifies live behavior without restarting.
- [ ] **Step 2: Update POST/PUT/DELETE endpoints** to persist then activate a candidate snapshot and return `active=true`, `requiresRestart=false`, and the active version.
- [ ] **Step 3: Preserve CAS/ETag and return the existing conflict responses without activation on stale mutations.**
- [ ] **Step 4: Run the integration test with PostgreSQL** and confirm it passes.

### Task 4: Update Desktop UX and contracts

**Files:**
- Modify: `src/AegisWAF.Desktop/Views/SitesView.xaml`
- Modify: `src/AegisWAF.Desktop/Views/SitesView.xaml.cs`
- Modify: `tests/AegisWAF.Tests/SiteManagerContractTests.cs`
- Modify: `tests/AegisWAF.Tests/UiContractTests.cs`

- [ ] **Step 1: Replace restart wording** with immediate activation status and preserve refresh/health feedback.
- [ ] **Step 2: Show live activation state** from `ActivationStatus`/`PendingRestart` without adding network polling.
- [ ] **Step 3: Run UI and API focused tests** and confirm they pass.

### Task 5: Verify and hand off

**Files:**
- Modify: `handoff.md`
- Create: `artifacts/module-verification/20261026-110000-multisite-module31-live-route-activation.json`

- [ ] **Step 1: Run solution build, focused PostgreSQL/API/UI/runtime tests, full available regression, and `git diff --check`.**
- [ ] **Step 2: Record Module 31 before/after percentages and evidence, including any full-regression baseline limitations.**
- [ ] **Step 3: Audit and document the next Multi-site opportunity.
