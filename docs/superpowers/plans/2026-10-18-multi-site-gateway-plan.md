# Multi-Site Gateway Implementation Plan

> **Implementation note:** Execute this plan in an isolated worktree. Use TDD for every behavioral change: write the failing test, run it and observe the failure, implement the smallest change, then run the focused and full suites.

**Spec:** `docs/superpowers/specs/2026-10-18-multi-site-gateway-design.md`  
**Goal:** เปลี่ยน Gateway จาก single-site/single-upstream เป็น multi-site ที่แยก routing, policy, rate limit, egress, health และ observability ต่อ Site โดยยังรองรับ configuration เดิม

## Global constraints

- Do not add MFA.
- Preserve the existing admin authorization model.
- Default Desktop behavior must remain usable through a generated `default-site`.
- Production mode is fail-closed for unknown hosts, invalid Site configuration and unsafe upstreams.
- Never use the request Host header as an upstream URL.
- Do not commit generated `bin/`, `obj/`, `dist/` or test artifacts.
- Keep public API responses free of upstream credentials and internal connection details.

## Files and responsibilities

Create or modify only the following areas unless a test proves another file is required:

- `src/AegisWAF.Gateway/Sites/SiteModels.cs` — immutable Site, upstream and policy contracts.
- `src/AegisWAF.Gateway/Sites/SiteConfiguration.cs` — configuration binding, legacy migration and validation.
- `src/AegisWAF.Gateway/Sites/SiteMatcher.cs` — normalized hostname/path matching and deterministic precedence.
- `src/AegisWAF.Gateway/Sites/SiteContext.cs` — per-request resolved context.
- `src/AegisWAF.Gateway/Sites/SiteRegistry.cs` — validated snapshot and optional safe reload boundary.
- `src/AegisWAF.Gateway/Sites/SiteHealth.cs` — bounded upstream health state and selection.
- `src/AegisWAF.Gateway/GatewayApp.cs`, `ProductionSecurity.cs`, `Egress/UpstreamEgressPolicy.cs`, `SecurityEventLogging.cs`, `ProductionReadinessPolicy.cs`, `ProductionHealth.cs` — consume SiteContext and site policies.
- `src/AegisWAF.Gateway/Auth/*` and `database/009_distributed_security_limits.sql` only if the existing shared bucket schema needs a SiteId column/key update.
- `src/AegisWAF.Gateway/Program.cs`, `appsettings.json`, `appsettings.local.json.example` — defaults and example configuration.
- `src/AegisWAF.Gateway/Auth/SiteEndpoints.cs` — admin API for Site management.
- `src/AegisWAF.Client/ApiClient.cs`, `Models.cs` — client contracts and API calls.
- `src/AegisWAF.Desktop/Views/SitesView.xaml`, `SitesView.xaml.cs`, `MainWindow.xaml(.cs)` — Site Manager navigation and UX.
- `tests/AegisWAF.Tests/Site*.cs`, `GatewayFixture.cs` and focused existing tests — proof of behavior.
- `docs/PROTECTION.md`, `docs/PRODUCTION_READINESS.md`, `docs/ARCHITECTURE.md`, `README.md` — operator guidance.

## Task 1 — Site contracts and safe configuration snapshot

**Tests first**

- Add `SiteConfigurationTests` for valid binding, disabled Sites, duplicate hostname rejection, duplicate active path overlap rejection, missing upstream rejection, invalid scheme/credentials rejection and Production HTTPS/egress requirements.
- Add a regression test proving legacy `ReverseProxy` configuration becomes one `default-site` with the same upstream.
- Run the focused tests and verify they fail because the Site contracts/resolver do not exist.

**Implementation**

- Add immutable records for `GatewaySite`, `SiteUpstream`, `SiteRateLimitPolicy`, `SiteEgressPolicy` and a `SiteConfigurationSnapshot`.
- Implement configuration binding under `Sites` with bounded values and normalized IDs/hostnames/paths.
- Implement legacy migration only when `Sites` is absent; reject ambiguous mixed configuration in Production.
- Validate exact host conflicts, path conflicts, upstream URL credentials, allowed schemes/ports and explicit egress hosts.
- Expose a validated snapshot through `SiteRegistry` without allowing partially valid configuration to enter runtime.

**Verification**

- Focused Site configuration tests pass.
- Existing production readiness tests remain green.

**Commit**

`Add multi-site configuration contracts and validation`

## Task 2 — Deterministic Host/path matcher and request context

**Tests first**

- Add matcher tests for case-insensitive host normalization, default ports, exact hostname matching, unknown host, disabled Site, longest path prefix and ambiguous match rejection.
- Add adversarial tests for `evil-example.com`, trailing dots, encoded paths and Host values with unexpected ports.
- Run focused tests and observe failure.

**Implementation**

- Implement `SiteMatcher` with exact matching by default and safe path normalization.
- Return a typed result containing SiteId, matched hostname and path prefix; never return an upstream directly from untrusted input.
- Add `SiteContext` access through `HttpContext.Items` or a typed accessor with a single request lifetime.
- Define fail-closed response mapping for unknown/disabled/invalid Site.

**Verification**

- Focused matcher tests pass.
- No existing admin/static route is captured by proxy Site matching.

**Commit**

`Add deterministic multi-site request matching`

## Task 3 — Integrate SiteContext into proxy and security pipeline

**Tests first**

- Extend gateway integration tests with Host A -> upstream A, Host B -> upstream B, unknown Host fail-closed and Host A cannot select B.
- Add tests proving the selected Site policy controls request body inspection, response inspection, request sanitizer and production egress checks.
- Add tests proving proxy routes receive SiteId in the request context.
- Run focused tests and observe failure.

**Implementation**

- Resolve SiteContext before proxy traffic reaches `MapReverseProxy`.
- Build/validate YARP route and cluster selection from the resolved Site snapshot, or introduce a controlled destination transform that only uses the validated upstream selected by SiteRegistry.
- Preserve admin/API/static route behavior and isolate it from Site proxy routing.
- Return generic fail-closed errors without revealing Site inventory or upstream details.
- Ensure request body buffering and response inspection continue to operate with Site policy limits.

**Verification**

- Multi-site gateway integration tests pass.
- Existing 214-test baseline remains green or any changed expectation is documented in the test.

**Commit**

`Route proxy traffic through site-aware security context`

## Task 4 — Site-scoped rate limiting, egress and events

**Tests first**

- Add rate-limit tests proving identical IPs on Site A and Site B do not share per-Site buckets.
- Add PostgreSQL key/schema tests proving SiteId is included in shared bucket identity and route identity.
- Add egress tests proving Site A cannot use Site B's allowed host and private/metadata destinations remain blocked.
- Add event tests proving `siteId`, normalized host and upstreamId are present without secrets.
- Run focused tests and observe failure.

**Implementation**

- Change `SlidingRateLimiter`/`PostgresRateLimiter` interfaces to accept SiteId and use bounded site+route keys.
- Update the distributed SQL migration only if required; preserve old data migration behavior and add indexes/constraints for the new key.
- Resolve `UpstreamEgressPolicy` from the Site policy, not global configuration, while retaining global hard safety blocks.
- Extend `SecurityEvent` and structured audit persistence with SiteId/UpstreamId fields using backward-compatible nullable columns or migration.
- Ensure event cardinality and log payloads remain bounded.

**Verification**

- Focused security tests pass.
- Production readiness still requires shared PostgreSQL stores when enforcement is enabled.

**Commit**

`Isolate rate limiting egress and events per site`

## Task 5 — Upstream health and safe selection

**Tests first**

- Add tests for one healthy upstream, multiple weighted upstreams, unhealthy fallback, all-unhealthy `503`, timeout and bounded response handling.
- Add tests proving health probes cannot carry credentials and cannot probe disallowed private/metadata destinations.
- Run focused tests and observe failure.

**Implementation**

- Implement `SiteHealth` with per-Site/upstream state, bounded timeout, failure backoff and an atomic snapshot.
- Implement deterministic single-upstream selection and weighted selection for multiple healthy destinations.
- Integrate selection with proxy execution without allowing stale or unvalidated destinations.
- Add health/readiness output with SiteId and safe public status.

**Verification**

- Health and selection tests pass.
- No unbounded probe, retry or destination list is introduced.

**Commit**

`Add site upstream health and failover selection`

## Task 6 — Admin API and Desktop Site Manager

**Tests first**

- Add API authorization tests for list/read/create/update/disable/health-check.
- Add validation tests for hostname overlap, invalid upstream and egress policy.
- Add UI contract tests for Site Manager navigation, OWASP protection icon, health state and Production warning.
- Run focused tests and observe failure.

**Implementation**

- Add `SiteEndpoints` under the existing admin guard; use soft-disable rather than destructive deletion by default.
- Add client models/API methods and error mapping.
- Add `SitesView` with list/detail/edit forms, safe defaults, health check action, policy indicators and explicit pending-reload state.
- Keep MFA unchanged/skipped and reuse existing session/RBAC rules.
- Ensure API does not reveal credentials or internal upstream connection strings.

**Verification**

- API and UI contract tests pass.
- Desktop Release build succeeds.

**Commit**

`Add multi-site administration and desktop management`

## Task 7 — Production readiness, migration docs and end-to-end proof

**Tests first**

- Add readiness tests for no active Site, mixed legacy/Sites config, unknown host fail-closed, missing per-Site egress and unsafe upstream.
- Add end-to-end tests covering two Sites, policy isolation, health failure, event fields and rate-limit isolation.
- Run the focused suite and observe failures before final wiring.

**Implementation**

- Extend `ProductionReadinessPolicy` and `ProductionHealth` to report per-Site checks.
- Add sample multi-site config and migration instructions to `README.md`, `ARCHITECTURE.md`, `PROTECTION.md` and `PRODUCTION_READINESS.md`.
- Add security-loadtest scenarios for multiple Host headers/Sites, unknown host, cross-site upstream attempts and per-Site rate limits.
- Build Desktop publish to `D:\AegisWAF\dist\AegisWAF-Desktop` with only the final EXE in the publish directory.

**Verification**

- Full test suite passes with the final count recorded.
- `dotnet build`/publish succeeds with zero warnings and errors.
- `node --check` passes for any changed JavaScript.
- Security/load-test harness produces results; external penetration testing remains an explicit gate and is not claimed as completed by local tests.
- Verify final EXE hash, timestamp and that it contains the multi-site code path through the successful Desktop publish.

**Commit**

`Complete multi-site production readiness and documentation`

## Completion checklist

- [ ] All tasks completed with focused RED -> GREEN evidence.
- [ ] Full test suite passes.
- [ ] Desktop Release publish passes.
- [ ] Unknown Host and cross-site access are fail-closed in Production.
- [ ] Rate limiting, egress, events and health are Site-scoped.
- [ ] Legacy single-site configuration still works.
- [ ] No generated artifacts are committed.
- [ ] Production limitations and external testing gates are documented.

