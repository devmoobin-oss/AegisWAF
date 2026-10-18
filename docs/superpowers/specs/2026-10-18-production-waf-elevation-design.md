# AegisWAF Production WAF Elevation Design

Date: 2026-10-18

Status: Design approved in conversation; implementation not started.

## Intent and boundaries

AegisWAF will be advanced from a text-signature prototype toward a production-oriented WAF baseline across four connected capabilities:

1. parser-aware request inspection;
2. outbound network control for configured upstreams;
3. distributed authentication and traffic throttling using PostgreSQL already required by the gateway;
4. repeatable security regression and load-test evidence.

MFA/WebAuthn remains intentionally out of scope for this initiative. The Desktop profile must remain usable with its current local defaults. Strict production behavior is opt-in through `Waf:Deployment:Enforce=true`; production enforcement must fail closed when a required control is missing.

This work improves defensive controls and test evidence. It does not constitute an independent penetration-test certification or guarantee that an arbitrary upstream application is secure.

## Success criteria

- Requests are represented as bounded, typed inspection targets rather than one concatenated raw string.
- JSON, form-urlencoded, XML, multipart metadata/fields, path and query components have explicit parser/normalizer behavior and regression coverage.
- Malformed, over-limit, unsupported, or decompression-dangerous input follows an explicit policy and cannot silently bypass inspection.
- Every production upstream is restricted by an explicit allowlist/policy and is checked at startup and before connection; private, loopback, link-local, metadata, multicast, reserved, and disallowed-port targets are rejected.
- DNS answers used for upstream connections are bounded, short-lived, and checked against the egress policy so DNS rebinding cannot move a permitted hostname to a forbidden address.
- Login abuse and WAF traffic limits use shared PostgreSQL state across instances, with per-IP, per-username, per-route, and global dimensions where configured.
- State-store failure is observable and fails closed for controls marked security-critical.
- The project has deterministic security regression tests plus a repeatable HTTP load/security harness with machine-readable results.
- Existing Desktop startup and local development defaults continue to work without requiring Redis, MFA, or external security tooling.

## Architecture

### 1. Parser-aware inspection pipeline

Introduce focused components behind stable interfaces:

- `RequestInspectionContext`: immutable request metadata and bounded raw segments; never exposes secrets in logs.
- `RequestBodyInspector`: reads at most the configured compressed and decompressed budgets, preserving the request body for upstream forwarding when inspection succeeds.
- `StructuredPayloadParser`: parses JSON, XML, form-urlencoded, and multipart fields/metadata into bounded `InspectionValue` records.
- `InspectionNormalizer`: applies URL decoding, HTML decoding, Unicode normalization, comment/whitespace transforms, and content-type-specific transforms without mutating forwarded data.
- `RuleEngine`: evaluates built-in and custom rules against typed targets and returns a structured decision containing rule ID, category, OWASP mapping, confidence, and action.

The current regex engine remains an evaluation backend initially. The interface must allow future CRS-compatible rule backends without changing middleware. Raw fallback inspection is allowed only when the content type is unknown and the configured policy explicitly permits it; unsupported structured content in strict production mode is rejected or routed to a configured audit-only mode, never silently treated as clean.

Compressed bodies are supported only through bounded decompression. The limits are independent for compressed bytes, decompressed bytes, expansion ratio, parser nesting depth, object/array member count, multipart part count, and inspection time. A limit breach produces a distinct security event and a 413/403 policy result as configured.

### 2. Upstream egress control

Add an `IUpstreamEgressPolicy` used by configuration validation, readiness, and the proxy connection path. The policy contains:

- allowed scheme set, normally HTTPS in production;
- allowed destination hostnames/CIDRs;
- allowed destination ports;
- private/reserved/metadata address denial;
- DNS resolution timeout and bounded cache;
- optional hostname-to-address pinning for the lifetime of a connection/request;
- explicit exception records for private internal origins when an operator chooses to allow them.

Production startup rejects an upstream that is not covered by policy. Runtime connection checks reject a changed DNS answer or an address outside the policy and emit an audit/security event. The policy must not use user-controlled `Host`, `X-Forwarded-*`, or redirect values to broaden egress permissions.

The Desktop default `http://localhost:8080/` remains available only when production enforcement is disabled. Production deployments must configure explicit upstream policy and should use infrastructure egress ACLs as a second control.

### 3. Distributed authentication and rate limiting

Extend the PostgreSQL-backed state model with bounded, indexed counters for:

- failed login attempts by normalized username and source identity;
- per-IP and per-username login windows;
- per-IP, per-route, and global WAF request windows;
- cleanup timestamps and schema versioning.

Updates must be atomic and transaction-safe. A rejected request must not consume unrelated global capacity unless the configured algorithm explicitly documents that behavior. The API returns `Retry-After` only when the value is safe to disclose. Client identity comes from the existing trusted-proxy resolver; untrusted forwarded headers are never accepted.

The in-memory limiter remains a Desktop fallback. Production enforcement requires PostgreSQL shared state, migration readiness, bounded key cardinality, cleanup, and a fail-closed circuit breaker. Authentication throttling must cover the login endpoint itself and must not rely solely on per-process memory.

### 4. Security and load-test proof

Add a test harness separated from production runtime code:

- deterministic unit tests for parser, normalization, egress, policy, and atomic limiter behavior;
- integration tests through the actual gateway for malformed framing, compressed payloads, multipart, response overflow, upstream failure, DNS changes, authentication abuse, and role boundaries;
- a load-test runner that can drive a local gateway with configurable concurrency, duration, request mix, payload size, and attack/clean ratio;
- machine-readable JSON/CSV output containing requests, status distribution, blocked events, errors, p50/p95/p99 latency, throughput, and resource observations;
- an external assessment guide for OWASP ZAP/WSTG and an evidence template that clearly separates automated evidence from independent testing.

The harness must avoid destructive payloads against real systems and default to synthetic local upstreams. No test is allowed to claim OWASP coverage merely because a string signature matched.

## Configuration and compatibility

New production settings must have safe defaults and be documented in `appsettings.json`, the local example, and `docs/PRODUCTION_READINESS.md`. Proposed groups:

- `Waf:Inspection:*` for parser, decompression, nesting, member, and time budgets;
- `Waf:Egress:*` for destination policy, DNS behavior, ports, and private-target exceptions;
- `Waf:RateLimit:*` and `Auth:Throttle:*` for shared dimensions and bounded cardinality;
- `Security:TrustedProxies` remains the only source for forwarded-client identity.

Production validation must reject contradictory settings, unsafe unbounded values, wildcard egress, unrestricted private exceptions, insecure auth throttle storage, or a disabled fail-closed action where the selected control requires it.

## Failure handling and observability

- Parser errors and budget breaches produce a stable reason code, OWASP mapping when applicable, request ID, and no raw secret payload.
- Egress denials include destination policy ID and resolved address only; credentials and full authorization headers are never logged.
- Shared-store outages fail closed for production-critical paths and expose a readiness failure plus an alertable structured event.
- Queue overflow, audit integrity failure, stale policy, and DNS-policy mismatch are high-priority events.
- Metrics distinguish blocked, allowed, parser-error, policy-denied, rate-limited, upstream-failed, and uninspected traffic.

## Rollout order

1. Parser contracts and failing tests, then structured inspection middleware.
2. Egress policy and connection enforcement, then readiness and audit integration.
3. PostgreSQL migrations and distributed auth/rate-limit paths, then failure-mode tests.
4. Load/security harness, documentation, external assessment guide, full verification, and Release single-file build.

Each phase must keep the full test suite green and must not claim production readiness until its external gates are explicitly recorded as pending or complete.

## Non-goals

- MFA/WebAuthn/OIDC implementation in this phase.
- Replacing the Desktop application with a web-only product.
- Creating a cloud service or mandatory Redis dependency.
- Treating OWASP badges or automated tests as certification.
- Automatically changing upstream application code or database security.
