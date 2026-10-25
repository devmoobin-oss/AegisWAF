# CIDR-aware IP Policy Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** เพิ่ม IP allow/block policy แบบ exact IP และ CIDR พร้อม deterministic precedence, API deletion และเอกสารที่ตรงกับระบบจริง

**Architecture:** Reuse `Cidr` สำหรับ parse/normalize/match และให้ `IpAccessPolicy` เก็บ normalized policies แล้วเลือก exact หรือ longest-prefix match. Gateway จะรับ CIDR ผ่าน PUT และเพิ่ม query-based DELETE โดยคง legacy exact route; Desktop จะยอมรับ IP/CIDR ก่อนส่ง API.

**Tech Stack:** C#/.NET 10, ASP.NET Minimal APIs, PostgreSQL text policy keys, xUnit, existing module verifier.

**Spec:** `docs/superpowers/specs/2026-10-25-cidr-ip-policy-design.md`

## Global Constraints

- `allow` เป็น allowlist decision เท่านั้น และต้องไม่ข้าม signature inspection หรือ rate limiting
- ต้อง normalize network bits ก่อน persist และเลือก longest-prefix match เมื่อ CIDR ซ้อนกัน
- ต้องคง backward compatibility ของ exact-IP API route
- ต้องมีหลักฐาน build/test/API contract ก่อนรายงานโมดูลเกิน 98%

## Review Focus

- IPv4 CIDR ที่มี host bits — ต้อง normalize และ match เฉพาะ network เดียวกัน (`Ip_policy_normalizes_ipv4_cidr_network`)
- IPv6 CIDR — ต้อง mask bytes และ match ไม่ข้าม prefix (`Ip_policy_matches_ipv6_cidr`)
- exact กับ broad CIDR ที่ action ต่างกัน — exact ต้องชนะ (`Exact_policy_wins_over_broad_cidr`)
- CIDR deletion ผ่าน query และ exact deletion ผ่าน legacy route — ทั้งสองต้องลบ persisted/in-memory policy เดียวกัน (`Api_accepts_and_deletes_cidr_policy`)
- allow กับ malicious request — allow ต้องยังถูก inspection/rate limit (`Ip_allow_does_not_bypass_protection`)

---

### Task 1: Add failing CIDR policy tests

**Files:**
- Create: `tests/AegisWAF.Tests/IpPolicyTests.cs`
- Modify: `tests/AegisWAF.Tests/CatalogVerificationTests.cs` only if the integration regression needs a shared helper

**Interfaces:**
- Consumes: `IpAccessPolicy`, `Cidr`, `GatewayFixture`, `Api`
- Produces: named unit and integration tests that fail against exact-IP-only behavior

- [ ] **Step 1: Write tests** for IPv4 normalization/matching, IPv6 matching, exact precedence, invalid CIDR rejection, and API CIDR save/query-delete.
- [ ] **Step 2: Run the focused test filter** and confirm failures are caused by missing CIDR behavior, not test setup.

### Task 2: Implement normalized CIDR matching

**Files:**
- Modify: `src/AegisWAF.Gateway/ProductionSecurity.cs` (`Cidr` parse/normalize helpers)
- Modify: `src/AegisWAF.Gateway/Program.cs` (`IpAccessPolicy` storage/matching and policy loading)

**Interfaces:**
- Consumes: existing `Cidr.Contains` and `IPAddress`
- Produces: `Cidr.TryNormalize(string, out string)`, `Cidr.PrefixLength(string)`, and `IpAccessPolicy.Check(string)` with exact/longest-prefix semantics

- [ ] **Step 1: Implement the smallest parse/normalize helpers** needed by the failing tests.
- [ ] **Step 2: Implement policy matching and deterministic precedence** without changing block/allow middleware order.
- [ ] **Step 3: Run `IpPolicyTests`** and confirm all focused tests pass.

### Task 3: Extend persistence, API and Desktop input

**Files:**
- Modify: `src/AegisWAF.Gateway/GatewayApp.cs` (CIDR PUT validation, query DELETE, normalized audit/persistence)
- Modify: `src/AegisWAF.Client/ApiClient.cs` (CIDR-safe deletion URL)
- Modify: `src/AegisWAF.Desktop/Views/IpView.xaml.cs` (IP/CIDR validation and copy)

**Interfaces:**
- Consumes: `Cidr.TryNormalize`
- Produces: exact route compatibility and `DELETE /api/ip-policies?ip=...` for CIDR policies

- [ ] **Step 1: Accept normalized exact/CIDR policy keys and reject malformed networks with 400.**
- [ ] **Step 2: Add query deletion while retaining the legacy exact-IP route.**
- [ ] **Step 3: Update client/UI validation and run API/integration tests.**

### Task 4: Align docs and module verification

**Files:**
- Modify: `docs/PROTECTION.md`, `docs/SECURITY.md`, `docs/DEVELOPMENT_ROADMAP.md`, `src/AegisWAF.Client/ProtectionCatalog.cs`
- Modify: `tools/module-verification/modules.json`
- Modify: `handoff.md`

- [ ] **Step 1: Replace stale exact-IP/no-CIDR claims with the implemented behavior and retain the non-bypass warning.**
- [ ] **Step 2: Add module 9 `cidr-ip-policy` with gateway build, focused tests, API contract and docs gates.**
- [ ] **Step 3: Record before/after percentages and evidence in `handoff.md`.**

### Task 5: Full verification and commit

- [ ] **Step 1: Run full test suite and capture pass/fail counts.**
- [ ] **Step 2: Run all-module verifier with `-All -UpdateHandoff` and confirm every module is >98%.**
- [ ] **Step 3: Review diff, commit scoped changes, and verify clean status.**
