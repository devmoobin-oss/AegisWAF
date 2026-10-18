# AegisWAF Layered OWASP Defense & Coverage UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** เพิ่ม layered protection ที่ตรวจสอบได้จริงและทำให้ UI แสดง OWASP Top 10:2021 coverage, limitation และเหตุผลการบล็อกอย่างโปร่งใส

**Architecture:** แยก source of truth ของ OWASP/rule metadata ออกจาก regex engine, เพิ่ม `SsrfGuard` และ `AuthThrottle` เป็น policy components ที่ไม่มี network side effects, เสริม security headers เฉพาะ admin-owned responses และ expose read-only coverage API ให้ browser console ใช้ข้อมูลเดียวกับ server

**Tech Stack:** C# / ASP.NET Core .NET 10, xUnit, PostgreSQL integration tests, vanilla HTML/CSS/JavaScript

**Spec:** `docs/superpowers/specs/2026-10-18-owasp-defense-design.md`

## Global Constraints

- ใช้ OWASP Top 10:2021 เป็น taxonomy; badge ไม่ใช่ certification
- ไม่ส่ง request ออกภายนอกเพื่อทดสอบหรือ resolve DNS ใน WAF path
- ไม่เก็บ password, session token, CSRF token หรือ request body ทั้งก้อนใน event/audit
- Guard ที่ malformed ต้อง block เฉพาะเมื่อมีเหตุผลชัดเจน; timeout ต้อง fail closed ตาม policy เดิม
- ต้องคง API เดิมที่ผู้ใช้ใช้งานอยู่ให้ backward-compatible เมื่อทำได้
- ห้าม inject CSP/cache policy แบบกว้างใส่ upstream response
- ต้องเขียน failing test ก่อน production code ในทุก behavior change
- ต้องรัน `dotnet test` เต็มชุดเมื่อ environment มี .NET SDK และรัน Impeccable detector หลังแก้ UI

## Review Focus

- Encoded/Unicode/IPv6 SSRF targets ต้องถูกจัดการเหมือนรูปแบบตรง ๆ — ครอบคลุมใน Task 2 tests
- Username casing และ IP เปลี่ยนรูปต้องไม่ bypass auth throttle — ครอบคลุมใน Task 3 tests
- Admin headers ต้องไม่รั่วไปยัง upstream binary/stream response — ครอบคลุมใน Task 4 tests
- Rule ที่ไม่มี mapping หรือ coverage เป็น partial ต้องแสดงอย่างตรงไปตรงมา ไม่เดาจากชื่อ — ครอบคลุมใน Task 1 และ Task 5 tests
- UI ต้องประกาศ state และ badge ให้ keyboard/screen reader ใช้ได้ — ครอบคลุมใน Task 5 UI contract tests

### Task 1: Create the OWASP metadata source of truth

**Files:**
- Create: `src/AegisWAF.Gateway/OwaspCatalog.cs`
- Modify: `src/AegisWAF.Gateway/Program.cs`
- Modify: `src/AegisWAF.Client/Models.cs`
- Modify: `src/AegisWAF.Client/ProtectionCatalog.cs`
- Test: `tests/AegisWAF.Tests/OwaspMappingTests.cs`

**Interfaces:**
- Produces `OwaspCode`, `OwaspCategory`, `CoverageState`, `RuleSecurityMetadata`, `ProtectionCoverageItem` and `OwaspCatalog.All`
- `SecurityRules.List()` returns rule metadata including `OwaspCodes`, `Coverage` and `Description`
- Later tasks consume `OwaspCatalog.ForRule(string ruleId)` and `OwaspCatalog.ForCategory(string category)`

- [ ] **Step 1: Write the failing tests**

  Add tests that assert all ten codes exist exactly once, known rules map to the expected codes, A02/A04/A06 are not falsely marked runtime-covered, and an unknown rule returns `NotCovered` with a limitation rather than throwing.

- [ ] **Step 2: Run the mapping tests and verify they fail**

  Run: `dotnet test tests/AegisWAF.Tests/AegisWAF.Tests.csproj --filter FullyQualifiedName~OwaspMappingTests`

  Expected: FAIL because the catalog types and metadata fields do not exist yet.

- [ ] **Step 3: Implement the catalog and DTO extensions**

  Define stable string codes `A01` through `A10`, use immutable records, map SQLi/NoSQL/command/SSTI/XXE to A03, SSRF to A10, auth/admin controls to A01/A07, logging to A09, and mark non-runtime categories explicitly. Extend client DTOs with optional metadata so existing JSON consumers can continue reading old fields.

- [ ] **Step 4: Run the mapping tests and verify they pass**

  Run the same command. Expected: PASS with no duplicate code or unmapped built-in rule failures.

- [ ] **Step 5: Commit the metadata slice**

  Run: `git add src/AegisWAF.Gateway/OwaspCatalog.cs src/AegisWAF.Gateway/Program.cs src/AegisWAF.Client/Models.cs src/AegisWAF.Client/ProtectionCatalog.cs tests/AegisWAF.Tests/OwaspMappingTests.cs && git commit -m "feat: add OWASP protection metadata"`

### Task 2: Add parser-based SSRF protection

**Files:**
- Create: `src/AegisWAF.Gateway/SsrfGuard.cs`
- Modify: `src/AegisWAF.Gateway/Program.cs`
- Test: `tests/AegisWAF.Tests/SsrfGuardTests.cs`
- Test: `tests/AegisWAF.Tests/EmbeddedGatewayTests.cs`

**Interfaces:**
- `SsrfGuard.Check(string value) -> SsrfDecision`
- `SsrfDecision` contains `Blocked`, `Reason` and optional `OwaspCode` fixed to `A10`
- The request pipeline calls the guard for candidate values before forwarding; it never performs DNS resolution or outbound HTTP

- [ ] **Step 1: Write failing unit tests**

  Cover `http://127.0.0.1`, `http://10.0.0.4`, `http://[::1]`, `http://169.254.169.254`, `http://metadata.google.internal`, encoded `file://`, `gopher://`, userinfo forms, alternate ports, malformed URLs, public HTTPS URLs and ordinary non-URL text.

- [ ] **Step 2: Run the tests and verify expected failures**

  Run: `dotnet test tests/AegisWAF.Tests/AegisWAF.Tests.csproj --filter FullyQualifiedName~SsrfGuardTests`

  Expected: FAIL because `SsrfGuard` is not implemented.

- [ ] **Step 3: Implement `SsrfGuard.Check`**

  Extract URI candidates without network access, normalize percent encoding safely, reject dangerous schemes and special IP ranges, and return a stable reason. Keep ordinary text and public URL examples allowed. Do not treat every hostname as private without a configured allowlist.

- [ ] **Step 4: Add pipeline integration tests first**

  Add gateway probes proving private/metadata/dangerous-scheme values return 403 with `SSRF`/`A10` event metadata, while a public URL-shaped value does not trigger the guard.

- [ ] **Step 5: Integrate the guard into request inspection**

  Run the guard over the same normalized query/body/header/cookie candidate values used by signatures. Preserve explicit IP allow policy behavior and keep inspection bounded by existing size limits.

- [ ] **Step 6: Run focused and existing protection tests**

  Run the SSRF filter plus `CatalogVerificationTests`. Expected: all SSRF cases pass and no existing catalog expectation changes without a documented reason.

- [ ] **Step 7: Commit the SSRF slice**

  Run: `git add src/AegisWAF.Gateway/SsrfGuard.cs src/AegisWAF.Gateway/Program.cs tests/AegisWAF.Tests/SsrfGuardTests.cs tests/AegisWAF.Tests/EmbeddedGatewayTests.cs && git commit -m "feat: add parser-based SSRF guard"`

### Task 3: Add authentication throttling and audit outcomes

**Files:**
- Create: `src/AegisWAF.Gateway/Auth/AuthThrottle.cs`
- Modify: `src/AegisWAF.Gateway/Auth/AuthEndpoints.cs`
- Modify: `src/AegisWAF.Gateway/GatewayApp.cs`
- Test: `tests/AegisWAF.Tests/AuthThrottleTests.cs`
- Test: `tests/AegisWAF.Tests/AuthenticationTests.cs`

**Interfaces:**
- `AuthThrottle.Allow(string clientIp, string username, DateTimeOffset now) -> ThrottleDecision`
- `ThrottleDecision` contains `Allowed`, `RetryAfter`, `Reason` and no account-existence information
- Auth endpoints record `auth.throttled` and `auth.locked` audit actions without storing secrets

- [ ] **Step 1: Write failing unit tests**

  Assert that failed attempts are bounded per normalized IP+username, casing/whitespace normalization cannot bypass the key, successful login resets the failure bucket, the limiter expires windows, and a single IP cannot consume an unbounded dictionary.

- [ ] **Step 2: Run focused tests and verify failure**

  Run: `dotnet test tests/AegisWAF.Tests/AegisWAF.Tests.csproj --filter FullyQualifiedName~AuthThrottleTests`

  Expected: FAIL because the throttle component does not exist.

- [ ] **Step 3: Implement bounded `AuthThrottle`**

  Use a concurrent in-memory window with bounded cleanup, configurable limits under `Auth:Throttle`, and conservative defaults. Keep existing account lockout as a separate defense.

- [ ] **Step 4: Integrate into login flow**

  Check throttle before password verification, return the same generic failure response for unknown user, bad password and throttled requests, reset only after verified success, and write audit outcomes without password/token data.

- [ ] **Step 5: Add endpoint tests**

  Test repeated failures, casing variants, success reset, generic response text, audit action and existing session behavior.

- [ ] **Step 6: Run authentication and authorization suites**

  Run: `dotnet test tests/AegisWAF.Tests/AegisWAF.Tests.csproj --filter "FullyQualifiedName~AuthenticationTests|FullyQualifiedName~AuthorizationTests|FullyQualifiedName~AuthThrottleTests"`

- [ ] **Step 7: Commit the auth slice**

  Run: `git add src/AegisWAF.Gateway/Auth/AuthThrottle.cs src/AegisWAF.Gateway/Auth/AuthEndpoints.cs src/AegisWAF.Gateway/GatewayApp.cs tests/AegisWAF.Tests/AuthThrottleTests.cs tests/AegisWAF.Tests/AuthenticationTests.cs && git commit -m "feat: throttle authentication failures"`

### Task 4: Add admin security headers and coverage API

**Files:**
- Create: `src/AegisWAF.Gateway/AdminSecurityHeaders.cs`
- Modify: `src/AegisWAF.Gateway/GatewayApp.cs`
- Test: `tests/AegisWAF.Tests/AdminSecurityHeadersTests.cs`
- Test: `tests/AegisWAF.Tests/AuthorizationTests.cs`

**Interfaces:**
- `AdminSecurityHeaders.Apply(HttpContext context)` applies headers only to admin-owned UI/API/auth responses
- `GET /api/owasp-coverage` returns `IReadOnlyList<ProtectionCoverageItem>` for `VIEWER+`

- [ ] **Step 1: Write failing tests**

  Assert admin HTML/API responses include `nosniff`, `no-referrer`, restrictive `Permissions-Policy`, CSP compatible with the existing console, and `no-store` on session/login/admin API responses. Assert upstream/binary responses are not modified by admin-only headers.

- [ ] **Step 2: Run focused tests and verify failure**

  Run: `dotnet test tests/AegisWAF.Tests/AegisWAF.Tests.csproj --filter FullyQualifiedName~AdminSecurityHeadersTests`

  Expected: FAIL because the header component and endpoint do not exist.

- [ ] **Step 3: Implement route-aware headers**

  Apply headers after route ownership is known, avoid overwriting explicit safe headers, and keep the CSP policy aligned with the current inline SVG/JS console implementation. Do not apply admin policy to protected upstream content.

- [ ] **Step 4: Implement `/api/owasp-coverage`**

  Add a viewer-authorized read-only endpoint that serializes the source-of-truth catalog, rule IDs, coverage states, limitations and official documentation URL. Keep the payload stable and avoid exposing internal regex patterns.

- [ ] **Step 5: Run focused endpoint tests**

  Verify role gating, response schema, headers and upstream isolation.

- [ ] **Step 6: Commit the admin/API slice**

  Run: `git add src/AegisWAF.Gateway/AdminSecurityHeaders.cs src/AegisWAF.Gateway/GatewayApp.cs tests/AegisWAF.Tests/AdminSecurityHeadersTests.cs tests/AegisWAF.Tests/AuthorizationTests.cs && git commit -m "feat: add admin security headers and OWASP API"`

### Task 5: Add OWASP icons, coverage view and event metadata to the browser console

**Files:**
- Modify: `src/AegisWAF.Gateway/wwwroot/index.html`
- Modify: `src/AegisWAF.Gateway/wwwroot/assets/app.js`
- Modify: `src/AegisWAF.Gateway/wwwroot/assets/style.css`
- Modify: `tests/AegisWAF.Tests/UiContractTests.cs`

**Interfaces:**
- `renderOwaspBadge(code, label, coverage) -> string` returns escaped accessible badge markup
- `loadOwaspCoverage()` consumes `/api/owasp-coverage`
- Rules and events render server-provided `owaspCodes`; UI never infers mapping from display name

- [ ] **Step 1: Extend UI contract tests first**

  Assert the page includes the shield icon symbol, a coverage nav entry, an accessible legend, a filter hook, `renderOwaspBadge`, `owaspCodes` handling and no-certification copy.

- [ ] **Step 2: Run the UI contract test and verify it fails**

  Run: `dotnet test tests/AegisWAF.Tests/AegisWAF.Tests.csproj --filter FullyQualifiedName~UiContractTests`

  Expected: FAIL because the OWASP UI hooks are not present.

- [ ] **Step 3: Add semantic OWASP badge/icon markup**

  Add a shield symbol and badges with visible code plus `aria-label`, coverage color that is not the only state signal, and a legend explaining mapping versus certification.

- [ ] **Step 4: Add coverage page and rule/event rendering**

  Fetch the coverage endpoint, render A01–A10 cards/table with limitations, add code/coverage filters to WAF Rules, and show event metadata without exposing request bodies. Preserve existing empty/loading/error states.

- [ ] **Step 5: Style desktop/mobile/accessibility states**

  Use existing tokens, ensure badges remain readable at 200% zoom, maintain 44px controls, preserve focus-visible styling, and add reduced-motion behavior for newly introduced transitions.

- [ ] **Step 6: Run UI contract, JavaScript syntax and detector checks**

  Run: `dotnet test tests/AegisWAF.Tests/AegisWAF.Tests.csproj --filter FullyQualifiedName~UiContractTests`

  Run: `node --check src/AegisWAF.Gateway/wwwroot/assets/app.js`

  Run: `C:\Users\Asus\.agents\skills\impeccable\scripts\impeccable.cmd detect --json src/AegisWAF.Gateway/wwwroot/index.html`

- [ ] **Step 7: Commit the browser UI slice**

  Run: `git add src/AegisWAF.Gateway/wwwroot/index.html src/AegisWAF.Gateway/wwwroot/assets/app.js src/AegisWAF.Gateway/wwwroot/assets/style.css tests/AegisWAF.Tests/UiContractTests.cs && git commit -m "feat: show OWASP coverage in security console"`

### Task 6: Update protection documentation and run the full verification gate

**Files:**
- Modify: `docs/PROTECTION.md`
- Modify: `README.md`
- Test: existing `tests/AegisWAF.Tests/*`

- [ ] **Step 1: Document exact behavior and limitations**

  Add the A01–A10 matrix, SSRF guard rules, auth throttling defaults, header scope, false-positive guidance and a clear statement that OWASP badges are coverage mapping, not certification.

- [ ] **Step 2: Run the complete test suite**

  Run: `dotnet test AegisWAF.slnx`

  Expected: 0 failed tests, including catalog verification, authentication, authorization and gateway integration tests.

- [ ] **Step 3: Run final static/UI verification**

  Run JavaScript syntax check, Impeccable detector, and inspect desktop/mobile renders if the gateway can be started. Record any detector advisory that is intentionally retained.

- [ ] **Step 4: Review the full diff against the spec**

  Verify every acceptance criterion, ensure no secrets or payload bodies entered logs, and confirm existing API behavior remains compatible.

- [ ] **Step 5: Commit documentation and final verification**

  Run: `git add docs/PROTECTION.md README.md && git commit -m "docs: document OWASP defense coverage"`

