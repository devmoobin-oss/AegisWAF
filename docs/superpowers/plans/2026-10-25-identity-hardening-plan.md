# Identity Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add source-IP-wide login throttling to the local authentication fallback and align security evidence with the implemented password/throttle behavior.

**Architecture:** Extend the existing `AuthThrottle` with independently named IP, username, and pair buckets. Keep production PostgreSQL behavior unchanged because it already checks separate IP and username keys.

**Tech Stack:** C#/.NET 10, xUnit, existing ASP.NET Core authentication pipeline.

**Spec:** `docs/superpowers/specs/2026-10-25-identity-hardening-design.md`

## Global Constraints

- Preserve the existing login response and account-lockout behavior.
- Keep the local throttle bounded and deterministic.
- Do not implement MFA/WebAuthn/OIDC in this module.
- Do not weaken the existing password validation rules.

## Review Focus

- Many usernames from one IP must hit the IP bucket.
- One username from many IPs must hit the username bucket.
- A successful login must clear the local buckets for the client so a valid user is not stranded by a transient burst.
- Expiry and max-entry pruning must remain bounded.

### Task 1: Add failing cross-identity throttle tests

**Files:**
- Modify: `tests/AegisWAF.Tests/AuthThrottleTests.cs`

- [ ] Add tests for one-IP/many-usernames, one-user/many-IPs, success reset boundaries, and bounded keys.
- [ ] Run focused tests and observe RED before implementation.

### Task 2: Implement namespaced throttle buckets

**Files:**
- Modify: `src/AegisWAF.Gateway/Auth/AuthThrottle.cs`

- [ ] Add IP, username, and pair bucket decisions with normalized bounded keys.
- [ ] Preserve success reset semantics for IP, username and pair buckets.
- [ ] Run focused tests and confirm GREEN.

### Task 3: Align identity documentation and module verifier

**Files:**
- Modify: `docs/SECURITY.md`
- Modify: `docs/PROTECTION.md`
- Modify: `docs/DEVELOPMENT_ROADMAP.md`
- Modify: `tools/module-verification/modules.json`
- Modify: `handoff.md`

- [ ] Document the local and distributed throttle dimensions accurately.
- [ ] Add an `identity-hardening` module with four weighted gates.
- [ ] Record before/after percentages and evidence.

### Task 4: Verify and commit

- [ ] Run focused auth tests and full test suite.
- [ ] Run all-module verifier and confirm every module is above 98%.
- [ ] Review diff and commit only this module.
