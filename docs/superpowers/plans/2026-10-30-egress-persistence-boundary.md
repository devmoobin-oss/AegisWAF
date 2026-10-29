# Multi-Site Egress Persistence Boundary Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Reject malformed or duplicate persisted Multi-Site egress allowlist values before a PostgreSQL row becomes a runtime `GatewaySite`.

**Architecture:** Keep the existing shared `ValidateSnapshot` gate and add equivalent fail-fast checks to `SitePersistenceCodec.FromRow`, which is the database materialization boundary. The codec will validate explicit normalized hosts, valid ports, and HTTP/HTTPS schemes, including duplicate detection, without silently rewriting persisted data.

**Tech Stack:** .NET 10, C#, xUnit in-process class runner, PostgreSQL array persistence model.

**Spec:** This plan is the Module 161 continuation of `handoff.md` after Module 160.

## Global Constraints

- Preserve the existing `GatewaySite` and `SitePersistenceRow` public/internal shapes.
- Reject invalid persisted data with `InvalidDataException`; do not silently repair database rows.
- Keep configuration, snapshot, editor, and persistence validation behavior aligned.
- Follow TDD: the focused test must fail before production code changes.
- Record Module 161 as 0% → 100% only after focused, regression, build, and diff verification.

## Review Focus

- Duplicate hosts differing by case or a trailing dot must be rejected by `FromRow`.
- Duplicate ports must be rejected by `FromRow`.
- Duplicate schemes differing by case must be rejected by `FromRow`.
- Wildcard, invalid, or empty persisted hosts must be rejected before materialization.
- Schemes other than HTTP/HTTPS must be rejected before materialization.

### Task 1: Persistence egress validation

**Files:**
- Modify: `src/AegisWAF.Gateway/Sites/SitePersistenceCodec.cs`
- Test: `tests/AegisWAF.Tests/SitePersistenceCodecTests.cs`
- Evidence: `artifacts/module-verification/module161-egress-persistence-boundary.json`
- Handoff: `handoff.md`

**Interfaces:**
- Consumes: `SitePersistenceRow` egress arrays in `SitePersistenceCodec.FromRow`.
- Produces: the same `GatewaySite` on valid rows; `InvalidDataException` with an egress-specific message on invalid rows.

- [ ] Write one focused test covering duplicate normalized hosts, duplicate ports, duplicate schemes, invalid/wildcard hosts, and invalid schemes.
- [ ] Run `SitePersistenceCodecTests` and verify the new test fails because malformed egress values are currently accepted.
- [ ] Implement the minimal validation in `FromRow` using the existing `NormalizeHostname` behavior and case-insensitive comparisons.
- [ ] Run the focused codec tests and verify all pass.
- [ ] Run the established Multi-Site regression, Release build, JavaScript syntax check, and `git diff --check`.
- [ ] Write the Module 161 evidence report and append the 0% → 100% record to `handoff.md`.
- [ ] Commit the source, tests, evidence, plan, and handoff record.

## Verification Commands

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/module-verification/Invoke-InProcClassTests.ps1 -Classes SitePersistenceCodecTests
dotnet build AegisWAF.slnx -c Release --no-restore --disable-build-servers -m:1 -p:NuGetAudit=false -p:MSBuildEnableWorkloadResolver=false -v:minimal
node --check src/AegisWAF.Gateway/wwwroot/assets/app.js
git diff --check
```

