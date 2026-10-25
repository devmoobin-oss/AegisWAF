# Admin Perimeter Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add trusted-proxy-aware admin perimeter metadata and optional dual-listener path isolation with regression evidence.

**Architecture:** Centralize effective scheme/host and listener classification in a small perimeter policy. Reuse it from AdminGuard, auth cookie creation, and the early gateway middleware; keep port isolation disabled by default for Desktop.

**Tech Stack:** ASP.NET Core .NET 10, WPF-compatible embedded gateway, xUnit, existing configuration and middleware pipeline.

**Spec:** `docs/superpowers/specs/2026-10-25-admin-perimeter-hardening-design.md`

## Global Constraints

- Preserve the existing loopback Desktop profile when listener ports are unset.
- Forwarded headers are trusted only when the direct peer matches `Security:TrustedProxies`.
- Do not implement MFA/WebAuthn/OIDC in this module.
- Do not claim external penetration-test or firewall evidence from unit tests.
- Preserve unrelated changes in `D:\AegisWAF\.worktrees\multi-site-gateway`.

## Review Focus

- A spoofed `X-Forwarded-Proto` from an untrusted peer must not make HTTP admin traffic appear secure.
- A trusted proxy's forwarded host must be used for Origin comparison without allowing an arbitrary host.
- Configuring only one listener port must not silently claim full isolation.
- The default zero-port Desktop configuration must remain compatible.
- Cookie `Secure` must agree with the same effective HTTPS decision used by AdminGuard.

### Task 1: Add failing perimeter policy tests

**Files:**
- Create: `tests/AegisWAF.Tests/AdminPerimeterTests.cs`
- Modify: `tests/AegisWAF.Tests/ProductionReadinessPolicyTests.cs`

- [ ] Write tests for trusted/untrusted forwarded scheme and host, dual-port path isolation, default compatibility, and production validation.
- [ ] Run the focused tests and confirm they fail because the policy/config contract does not exist.

### Task 2: Implement the shared perimeter policy

**Files:**
- Create: `src/AegisWAF.Gateway/AdminPerimeterPolicy.cs`
- Modify: `src/AegisWAF.Gateway/Auth/AdminGuard.cs`
- Modify: `src/AegisWAF.Gateway/Auth/AuthEndpoints.cs`
- Modify: `src/AegisWAF.Gateway/GatewayApp.cs`

- [ ] Add effective scheme/host helpers gated by `ClientAddress.IsTrusted`.
- [ ] Add listener-port classification and fail-closed production validation.
- [ ] Reuse the effective HTTPS result for remote admin and session cookie flags.
- [ ] Run focused tests and confirm they pass.

### Task 3: Document and register the module

**Files:**
- Modify: `src/AegisWAF.Gateway/appsettings.json`
- Modify: `docs/PRODUCTION_READINESS.md`
- Modify: `docs/SECURITY.md`
- Modify: `tools/module-verification/modules.json`
- Modify: `handoff.md`

- [ ] Document the production binding contract and Desktop compatibility.
- [ ] Add a module verifier entry with weighted build, focused tests, config contract, and documentation gates.
- [ ] Record before/after percentages and evidence in `handoff.md`.

### Task 4: Verify the module and whole repository

- [ ] Build gateway and test projects.
- [ ] Run focused perimeter tests and full test suite.
- [ ] Run the all-module verifier and confirm the new module is above 98%.
- [ ] Review the diff and commit only this module's files.
