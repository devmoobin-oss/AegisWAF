# Persisted Site Identity Boundary Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Make PostgreSQL Site materialization reject invalid identity and empty required collections before a malformed row becomes a runtime `GatewaySite`.

**Architecture:** Reuse `SiteConfigurationValidator`'s Site ID normalization/grammar and keep `SitePersistenceCodec.FromRow` as the fail-closed materialization boundary. Preserve the later snapshot validation as defense in depth.

**Tech Stack:** .NET 10, C#, xUnit in-process class runner.

**Spec:** Module 163 continuation from `handoff.md` after Module 162.

## Global Constraints

- Reject invalid Site IDs, empty hostname collections, and empty path-prefix collections with `InvalidDataException`.
- Canonicalize valid persisted Site IDs using the existing lower-case/trim normalization.
- Do not truncate or silently repair malformed persisted rows.
- Follow TDD and record 0% → 100% only after focused and related verification.

## Review Focus

- Whitespace and uppercase around a valid Site ID must produce the canonical lower-case ID.
- IDs containing spaces or invalid characters must fail closed.
- Empty hostname arrays must fail before runtime construction.
- Empty path-prefix arrays must fail before canonicalization.

### Task 1: Persistence identity and required collection validation

**Files:**
- Modify: `src/AegisWAF.Gateway/Sites/SiteConfiguration.cs`
- Modify: `src/AegisWAF.Gateway/Sites/SitePersistenceCodec.cs`
- Test: `tests/AegisWAF.Tests/SitePersistenceCodecTests.cs`
- Evidence: `artifacts/module-verification/module163-persisted-site-identity-boundary.json`
- Handoff: `handoff.md`

**Interfaces:**
- Consumes: `SitePersistenceRow` identity and required arrays.
- Produces: canonical `GatewaySite.Id` for valid rows and fail-closed `InvalidDataException` for invalid rows.

- [ ] Write focused RED tests for invalid IDs, empty required arrays, and valid ID canonicalization.
- [ ] Add the shared validator helper and fail-fast persistence checks.
- [ ] Run focused/related tests, Release build, JavaScript syntax, and diff checks.
- [ ] Record evidence and handoff progress, then publish the completed-modules artifact.

