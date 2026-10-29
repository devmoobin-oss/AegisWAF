# Persisted Site Cardinality Boundary Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Bound each persisted Multi-Site collection before `SitePersistenceCodec.FromRow` materializes a runtime site.

**Architecture:** Reuse the existing per-Site limits from `SiteConfigurationValidator` as internal shared constants. The persistence codec will reject oversized arrays before normalization, URI parsing, or runtime object construction; configuration and snapshot gates remain unchanged as defense in depth.

**Tech Stack:** .NET 10, C#, xUnit in-process class runner, PostgreSQL array persistence model.

**Spec:** This plan is the Module 162 continuation of `handoff.md` after Module 161.

## Global Constraints

- Preserve the existing persistence row and runtime model shapes.
- Fail closed with `InvalidDataException`; do not truncate or silently repair persisted arrays.
- Enforce the existing limits: 32 hostnames, 64 path prefixes, 32 upstreams, 64 egress hosts, 32 egress ports, and 4 egress schemes per Site.
- Check cardinality before iterating or materializing normalized values.
- Follow TDD and record 0% → 100% only after verification.

## Review Focus

- Oversized hostname arrays must fail before normalization.
- Oversized path-prefix arrays must fail before path canonicalization.
- Oversized upstream arrays must fail before URI parsing and array indexing.
- Oversized egress host and port arrays must fail before policy materialization.
- Oversized egress scheme arrays must fail before duplicate/scheme validation.

### Task 1: Persisted per-Site cardinality validation

**Files:**
- Modify: `src/AegisWAF.Gateway/Sites/SiteConfiguration.cs`
- Modify: `src/AegisWAF.Gateway/Sites/SitePersistenceCodec.cs`
- Test: `tests/AegisWAF.Tests/SitePersistenceCodecTests.cs`
- Evidence: `artifacts/module-verification/module162-persisted-site-cardinality.json`
- Handoff: `handoff.md`

**Interfaces:**
- Consumes: `SitePersistenceRow` arrays at `SitePersistenceCodec.FromRow`.
- Produces: fail-closed `InvalidDataException` for oversized collections and the existing `GatewaySite` for bounded valid rows.

- [ ] Add a focused test for each persisted collection limit and verify it fails before the production change.
- [ ] Make the existing validator limits internally reusable without changing their values.
- [ ] Add early cardinality checks in `FromRow` before collection iteration.
- [ ] Run focused and related Multi-Site tests, Release build, JavaScript syntax, and diff checks.
- [ ] Record the progress/evidence and commit the implementation.

