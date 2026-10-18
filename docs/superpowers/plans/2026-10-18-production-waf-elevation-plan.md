# AegisWAF Production WAF Elevation Implementation Plan

> **Execution note:** Follow the approved design at `docs/superpowers/specs/2026-10-18-production-waf-elevation-design.md`. Work test-first: every implementation task begins with a failing regression test, then the smallest implementation that makes it pass.

## Goal

Advance AegisWAF toward a production-oriented WAF baseline without breaking the Desktop profile, by adding parser-aware inspection, enforced upstream egress policy, PostgreSQL-backed distributed authentication/rate limiting, and repeatable security/load-test evidence. MFA/WebAuthn remains out of scope.

## Global constraints

- Keep the existing WPF Desktop application and local defaults usable.
- Production-only controls are gated by `Waf:Deployment:Enforce=true` and fail closed when required configuration is absent.
- Do not add Redis as a mandatory dependency.
- Never log raw passwords, cookies, authorization headers, full request bodies, or secret-bearing URLs.
- Do not mutate request bodies or URLs that are forwarded upstream.
- Use parameterized SQL and bounded work for all new code.
- Preserve OWASP mappings as coverage metadata, never as a certification claim.
- Do not implement MFA in this plan.

## Interfaces and file map

### Parser inspection

- Add `src/AegisWAF.Gateway/Inspection/InspectionModels.cs` for bounded inspection targets, parser decisions, reason codes, and structured rule decisions.
- Add `src/AegisWAF.Gateway/Inspection/InspectionNormalizer.cs` for content-aware canonical variants while reusing safe existing normalization behavior.
- Add `src/AegisWAF.Gateway/Inspection/StructuredPayloadParser.cs` for JSON, form-urlencoded, XML, and multipart extraction with depth/member/part limits.
- Add `src/AegisWAF.Gateway/Inspection/RequestBodyInspector.cs` for body buffering, bounded decompression, expansion-ratio enforcement, and body replay.
- Refactor `src/AegisWAF.Gateway/GatewayApp.cs` to call the inspection pipeline and preserve the current `SecurityRules` backend behind a stable decision interface.
- Update `src/AegisWAF.Gateway/AegisWAF.Gateway.csproj` only if a framework package is needed; prefer built-in .NET parsers and existing dependencies.

### Egress policy

- Add `src/AegisWAF.Gateway/Egress/UpstreamEgressPolicy.cs` for destination allowlists, ports, scheme rules, DNS checks, and address classification.
- Add `src/AegisWAF.Gateway/Egress/UpstreamEgressValidator.cs` for startup/readiness validation and runtime destination decisions.
- Integrate the runtime check at the YARP outbound connection boundary using the least invasive supported YARP/HttpClient hook; do not rely only on configuration validation.
- Update `src/AegisWAF.Gateway/ProductionReadinessPolicy.cs`, `ProductionHealth.cs`, `GatewayApp.cs`, and `appsettings.json`.

### Distributed auth/rate limiting

- Add `database/009_distributed_security_limits.sql` with versioned, indexed, bounded state for login and route-aware limits.
- Add `src/AegisWAF.Gateway/Auth/DistributedAuthThrottle.cs` and `src/AegisWAF.Gateway/DistributedRateLimiter.cs` with atomic PostgreSQL operations, cleanup, cardinality bounds, and fail-closed behavior.
- Update `AuthEndpoints.cs`, `AuthThrottle.cs`, `ProductionSecurity.cs`, `ProductionReadinessPolicy.cs`, `ProductionHealth.cs`, and `GatewayApp.cs`.
- Extend client IP/route identity handling without trusting untrusted forwarded headers.

### Proof and operations

- Add parser, egress, distributed-limit, and integration regression tests under `tests/AegisWAF.Tests`.
- Add `tools/security-loadtest/` with a safe local synthetic upstream, configurable runner, JSON/CSV result writer, and README.
- Add `docs/SECURITY_ASSESSMENT.md` for external OWASP ZAP/WSTG execution and evidence recording.
- Update `docs/PRODUCTION_READINESS.md`, `docs/PROTECTION.md`, `docs/PROGRESS.md`, and local configuration examples.

## Task 1: Establish parser contracts and production inspection settings

**Tests first**

- Add `tests/AegisWAF.Tests/InspectionModelTests.cs` covering target bounds, reason codes, and secret-safe decision formatting.
- Add `tests/AegisWAF.Tests/ProductionReadinessPolicyTests.cs` cases for invalid/unbounded inspection settings and safe Desktop defaults.
- Run the focused tests and verify they fail because the contracts/settings do not exist.

**Implementation**

- Define immutable records/enums for inspection targets, parser status, budget breach, and decision metadata.
- Add configuration parsing with bounded defaults for compressed bytes, decompressed bytes, expansion ratio, nesting depth, member count, multipart parts, and inspection time.
- Register the new services in `GatewayApp.AddServices`.
- Keep the current `SecurityRules` available as the first rule backend.

**Verification**

- Focused inspection and readiness tests pass.
- `dotnet build src/AegisWAF.Gateway/AegisWAF.Gateway.csproj -c Release --no-restore` passes.

## Task 2: Implement structured normalization and payload parsing

**Tests first**

- Add tests for JSON nested values, form keys/values, XML text/attributes/CDATA, multipart fields and filenames, malformed payloads, depth limits, member limits, and part limits.
- Add bypass regression cases for encoded nested values and parser disagreement between raw and structured representations.
- Run focused tests and verify failure before implementation.

**Implementation**

- Implement bounded parsers using built-in .NET APIs where available.
- Ensure XML external entity resolution is disabled and DTD behavior is explicitly rejected or safely bounded.
- Normalize each extracted value through the existing canonical transforms without changing the forwarded body.
- Return parser errors as explicit decisions rather than treating them as clean input.

**Verification**

- Focused parser tests pass, including malformed and adversarial cases.
- Existing `EngineUpgradeTests`, `FuzzRegressionTests`, and `CatalogVerificationTests` remain green.

## Task 3: Add bounded decompression and replay-safe request inspection

**Tests first**

- Add tests for gzip/deflate/Brotli accepted under limits, expansion-ratio breach, decompressed-size breach, malformed compressed input, timeout/cancellation, and body replay after inspection.
- Add integration tests proving blocked compressed requests do not reach the upstream and allowed requests preserve the original body semantics.
- Run focused tests and verify failure before implementation.

**Implementation**

- Replace the current blanket compressed-body rejection with a bounded decompression inspector for configured supported encodings.
- Enforce compressed byte, decompressed byte, ratio, CPU/time, and parser limits.
- Keep unsupported encodings fail-closed in production and configurable for Desktop compatibility.
- Refactor the request middleware to inspect typed targets, emit stable reason codes, and preserve the original stream for YARP.

**Verification**

- Integration tests prove no compressed bypass and no decompression bomb memory growth.
- Existing body-limit, framing, and proxy tests pass.

## Task 4: Integrate parser-aware rule decisions

**Tests first**

- Add end-to-end tests for SQLi/XSS/XXE/NoSQL/SSTI/traversal/command payloads in nested JSON, form, XML, multipart, query, cookie, and selected headers.
- Add tests that ordinary text and legitimate structured values are not blocked by parser extraction alone.
- Add tests that every decision contains rule ID/category/OWASP mapping and never includes raw secrets.

**Implementation**

- Adapt the current `SecurityRules` and custom-rule backend to evaluate each bounded inspection target.
- Preserve custom rule priority and toggle behavior.
- Record the first high-confidence block plus bounded evidence metadata; never persist raw payloads.
- Update event and UI DTO mapping to expose rule/OWASP/reason metadata safely.

**Verification**

- All new end-to-end cases pass.
- Existing UI contract, OWASP mapping, and security event tests pass.

## Task 5: Implement upstream egress policy

**Tests first**

- Add unit tests for scheme, port, hostname, CIDR, loopback, private, link-local, metadata, multicast, reserved, IPv4-mapped, and DNS-resolved targets.
- Add tests for allowed explicit internal exceptions and rejection of wildcard/unbounded exceptions.
- Add tests simulating DNS answer changes/rebinding.
- Run focused tests and verify failure before implementation.

**Implementation**

- Implement `IUpstreamEgressPolicy` and a bounded DNS/address evaluator, reusing hardened IP classification where safe.
- Validate every configured destination at startup and readiness.
- Enforce the same policy before outbound connection creation; do not trust user-controlled host or forwarding headers.
- Add policy IDs and safe destination metadata to audit/security events.
- Keep `http://localhost:8080/` valid only when production enforcement is disabled.

**Verification**

- Readiness rejects unapproved production destinations.
- Runtime tests prove DNS rebinding and private-target transitions are blocked.
- Proxy integration tests prove approved public/internal exceptions still route correctly.

## Task 6: Add distributed authentication throttling

**Tests first**

- Add integration tests proving failed login limits are shared across two gateway instances against one PostgreSQL database.
- Add tests for per-IP, per-username, combined-key, global, expiry, cleanup, bounded cardinality, and fail-closed database outage behavior.
- Add tests ensuring successful authentication resets only the intended failure dimensions.
- Run focused tests and verify failure before implementation.

**Implementation**

- Add migration `009_distributed_security_limits.sql` with schema tracking and indexes.
- Implement atomic upsert/consume operations for login windows and bounded cleanup.
- Apply the shared throttle before account lookup where practical while preserving constant-time password behavior.
- Return safe `Retry-After` values and generic authentication errors.
- Keep in-memory fallback for Desktop; production enforcement must require PostgreSQL shared auth throttling.

**Verification**

- Multi-instance integration tests pass with concurrent requests.
- Readiness and production policy reject production configuration without the new migration/store.

## Task 7: Add route-aware distributed WAF rate limiting

**Tests first**

- Add tests for per-IP, per-route, method-sensitive and global windows, atomic concurrency, expiration, cleanup, key cardinality, and transaction rollback.
- Add tests that rejected per-IP requests do not consume unrelated global capacity unless explicitly configured.
- Add tests for login, admin, upload, API, and proxy route policies.

**Implementation**

- Extend the PostgreSQL limiter key model with bounded route identifiers and policy IDs.
- Add route policy configuration and production validation.
- Apply the limiter to the login endpoint and protected traffic without bypassing admin controls.
- Add `Retry-After` and structured event fields without exposing identity secrets.
- Preserve the current Desktop in-memory behavior when production enforcement is false.

**Verification**

- Concurrent multi-instance limiter tests pass.
- Existing limiter, readiness, and authorization tests remain green.

## Task 8: Add security/load-test harness and external assessment guide

**Tests first**

- Add deterministic harness tests for result schema, percentile calculation, cancellation, safe payload selection, and non-destructive synthetic upstream behavior.
- Add a small integration smoke test that runs a clean/attack request mix against a local synthetic upstream.

**Implementation**

- Add a self-contained runner under `tools/security-loadtest/` with explicit concurrency/duration/size/mix arguments.
- Emit JSON and CSV results with throughput, latency percentiles, status distribution, blocked categories, parser errors, egress denials, rate-limit events, and upstream errors.
- Add `docs/SECURITY_ASSESSMENT.md` with ZAP/WSTG scope, safe target rules, evidence capture, and limitations.
- Add scripts for SAST, secret scan, dependency scan, SBOM, and load-test invocation where the environment supports them; scripts must report unavailable tools rather than silently pass.

**Verification**

- Harness smoke test produces valid machine-readable output.
- A documented local load run records results without contacting external systems.
- Dependency and secret scans are executed where available and their status is recorded.

## Task 9: Documentation, full verification, and release build

**Implementation**

- Update production configuration examples, readiness gates, protection limitations, progress ledger, and security assessment evidence template.
- Add migration/runbook instructions and explicit external gates still pending.
- Update the verification ledger under `.superpowers/sdd/2026-10-18-production-waf-elevation-plan/`.

**Verification**

- Run the full test suite and record exact passed/failed/skipped counts.
- Run Release build for Gateway, Desktop, and Tests as applicable.
- Run `dotnet publish` self-contained single-file for `win-x64` to `D:\AegisWAF\dist\AegisWAF-Desktop`.
- Verify the output folder contains only `AegisWAF.exe`, calculate SHA-256, and record size/hash.
- Run `node --check src/AegisWAF.Gateway/wwwroot/assets/app.js`.
- Run dependency vulnerability and secret scans; do not claim clean status when a tool could not run.
- Report implementation progress as completed engineering scope versus external production gates; never report 100% certification without independent testing.

## Final review focus

Before claiming completion, review specifically for:

- parser bypass through malformed/ambiguous content types;
- body mutation or body loss before proxying;
- decompression memory/CPU exhaustion;
- DNS rebinding between validation and connection;
- unbounded egress exceptions;
- rate-limit transaction leakage and key cardinality exhaustion;
- login endpoint bypassing shared throttling;
- secret/raw-payload leakage in logs and audit;
- Desktop defaults accidentally becoming fail-closed production defaults;
- tests that pass without proving the attack reached the real middleware path.
