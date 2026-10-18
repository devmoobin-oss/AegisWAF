# Runtime WAF Hardening Continuation Plan

## Goal

Close the highest-risk gaps found in the Production WAF review without changing the Desktop compatibility profile: make YARP use active/passive destination health for generated Sites, enforce upstream egress at the YARP HTTP-client connection boundary, and add production guardrails/tests that prove the controls are enabled only under the explicit production gate.

## Constraints

- MFA remains out of scope.
- Keep the local Desktop profile usable with `Waf:Deployment:Enforce=false`.
- Production must fail closed for disallowed scheme/host/port/address and health configuration errors.
- Do not log credentials, cookies, authorization headers, or full request bodies.
- No merge, push, dist publish, or external testing in this worktree.

## Tasks

### Task 1 — YARP health/failover configuration

Add RED tests proving generated Site clusters configure active health checks, passive checks, and `HealthyAndUnknown` destination selection. Implement generated config with bounded interval/timeout/path settings and production validation for invalid health values. Verify existing Site health tests and full suite.

### Task 2 — outbound egress connection boundary

Add RED tests for the outbound client factory/policy bridge: a destination must be denied before a socket is created when scheme, port, host, DNS answer, or address classification is disallowed; an approved address may connect. Implement a YARP `IForwarderHttpClientFactory` wrapper using bounded DNS resolution and `SocketsHttpHandler.ConnectCallback`, preserving TLS handling and connection pooling. Register it only for the gateway and emit safe policy metadata on denial.

### Task 3 — production guardrails and evidence

Add RED tests for production binding/default safety, health readiness, and safe egress failure behavior. Update production readiness/protection documentation and the ledger. Run focused tests, full suite, Gateway/Desktop Release builds, and diff checks. Record remaining gaps and percentage progress by recommendation category.

## Verification

- Every task starts with a failing test and ends with a focused passing run.
- Full suite remains green.
- Gateway and Desktop Release builds have zero warnings/errors.
- No production-complete claim is made until external security/load testing is performed.
