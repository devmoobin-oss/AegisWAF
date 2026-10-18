# Current Production WAF Progress Snapshot

Updated against the latest Production guardrail commit in the `feature/multi-site-gateway` worktree on 2026-10-19.

## Verified local evidence

| Evidence | Result |
|---|---|
| Full automated security/regression suite | **311 passed, 0 failed, 0 skipped** |
| Release solution build | **4 projects, 0 warnings, 0 errors** |
| Dependency vulnerability scan | **No vulnerable packages reported** by NuGet sources |
| Healthy-upstream real-socket load test | **82 requests; 78 blocked, 4 proxied, 0 errors** |
| Worktree integrity | **Clean; `git diff --check` passed** |

## Workstream progress

| Workstream | Progress | Evidence boundary |
|---|---:|---|
| Multi-Site routing and isolation | 99% | Real-socket site isolation and unknown-host rejection tested; external multi-instance validation pending |
| Runtime egress enforcement | 96% | Startup DNS preflight and connection-boundary checks tested; infrastructure ACL and external rebinding assessment pending |
| Upstream health/failover | 96% | Active/passive YARP health and local failover tested; external capacity/failure testing pending |
| Distributed rate limiting/auth throttling | 100% local evidence | PostgreSQL shared state and two-gateway tests pass; multi-instance deployment validation pending |
| Parser-aware hardening | 98% | Structured parsing, decompression limits, duplicate-key and fuzz regressions pass; independent bypass testing pending |
| Secure production guardrails | 100% local evidence | Fail-closed configuration/readiness checks pass; deployment operation review pending |
| Audit/SIEM integrity | 98% | Structured events, chain verification and tamper alert pass; SIEM routing/alert policy pending |
| External security assessment | 86% | Local harness exists; independent penetration, SAST/secret/SBOM and external load review pending |

These percentages describe implementation and local evidence only. They are not a certification or an OWASP compliance claim. The remaining gates are tracked in [PRODUCTION_READINESS.md](PRODUCTION_READINESS.md).

# Historical Progress Report — v0.7

## Iteration summary
* **Version**: v0.7 (from the v0.6 baseline)
* **Implemented**: per-user authentication (PBKDF2-SHA512), server-side sessions, lockout, CSRF, 5-role RBAC enforced per endpoint, deny-by-default `/api`, user-management API and GUI, administrator-initiated password reset, session revocation, change-password with forced-change flow, last-SUPER_ADMIN protection, audit with actor/IP/outcome, tracked embedded migrations, bootstrap admin from environment.
* **Defects found and fixed during the audit**: dashboard shadowed by the proxy catch-all; unmatched `/api` paths proxied upstream; weak admin network check; IP-policy delete not audited (details in `SECURITY.md`).
* **Database**: `006_auth_rbac.sql` plus `schema_migrations`. The local `aegiswaf` database was created and migrated 001–005 manually earlier in this session; with `Database:AutoMigrate` (default on) the app records 001–005 and applies 006 on its next start.
* **Tests added**: 53 xUnit tests — unit tests for the password hasher; integration tests through the real pipeline against a throw-away PostgreSQL database (role/endpoint matrix, CSRF, lockout, session revocation, non-local access, GUI served locally, migration idempotence).
* **Environment change**: the .NET SDK was not installed; 10.0.402 was installed with winget in order to build and test.

## Desktop app (added after the v0.7 iteration)
* `src/AegisWAF.Client` (typed admin-API client) and `src/AegisWAF.Desktop` (WPF app: login, local-gateway launcher, dashboard, events, rules + sandbox, IP policies, users, audit, change password). Test count is now 62 (9 new `ApiClient` integration tests against the real gateway pipeline).
* Verified by running the published exe with UI Automation on this machine: login window renders; login → main window; navigation to Rules / Users / Audit / IPs / Events; starting the gateway from the login window (health 200), logging in, and the gateway being stopped when the app closes.
* **Not verified**: write operations through the WPF UI (create user, delete rule, etc. — the underlying client calls are tested, the button handlers were not exercised), the forced-password-change window, session-expiry dialog, HiDPI/other-resolution layouts, behaviour on machines other than this one. WPF UI code has no automated tests.
* **Gateway embedded in AegisWAF.exe**: `Program.cs` was split into `GatewayApp` + `EmbeddedGateway` (no behaviour change; 62 prior tests still pass), the desktop app references the Gateway project and hosts it in-process. 3 new tests (start/serve/stop over a real socket, bad DB credentials, port in use) → 65 total. UI-Automation run of the published single exe confirmed, twice (first-run setup, then relaunch with auto-start from remembered settings): health 200, the listening socket is owned by `AegisWAF.exe`, no `AegisWAF.Gateway` process and no child processes exist, login works, and closing the app frees the port. DPAPI settings file contained no plaintext password. Not verified: DPAPI behaviour across Windows accounts, a non-loopback listen address (blocked in the UI by design), running two app instances (second would fail on the port).
* Defect found only by running the app: duplicate resource key `Muted` (Brush and Style) crashed startup at runtime although the build succeeded; fixed.

## Usability redesign of the desktop app (after the embedded-gateway change)
Reason: first-run setup was the hardest part (hand-written connection string, manual database creation, a separate bootstrap password, a second login, and a login window that mixed "sign in" with "start the gateway").
* New: 4-step setup wizard with plain-language database diagnostics and one-click database creation (`GatewaySetup`: validated/quoted database name, rejects hostile names before any SQL runs), structured settings (`GatewaySettings`, DPAPI), slim login window, settings page (edit/restart/reset), dashboard status banner + copyable address + **self-test** (`ApiClient.RunSelfTestAsync`), empty-state guidance, plain-language event explanations (`RuleText`), one-click **block IP** from events, rule descriptions, page hints, friendlier menu names.
* Tests: 87 total (22 new: database/upstream checks incl. hostile database names, self-test blocked/not-blocked, text mapping).
* Verified by running the published exe with UI Automation from a state with no settings file and no database: wizard → missing database detected → created → weak/good password hints → upstream check → summary → automatic start + sign-in → dashboard banner → self-test reported "blocked" → events/settings pages open → app closed → relaunch showed the slim login with the gateway already started → login succeeded. Test artefacts (settings file, database) were removed afterwards.
* Not verified: the "wrong database password" and "port already in use" messages in the wizard UI (covered by unit/integration tests of the underlying calls, and the wrong-password message was seen in an earlier run), the block-IP button, the settings-page save/restart and reset flows, the remote-gateway mode, high-DPI layouts. No usability testing with real users has been done — the redesign is based on the friction I met while using the app, not on observed users.

## Password policy removed (operator request)
Server (`PasswordHasher.Validate` is now only non-empty ≤128 chars), desktop wizard/change-password/users screens and the web console no longer mention or enforce length/complexity/username/"must differ" rules. Tests rewritten to assert the new behaviour (96 total): weak passwords such as `1`, `x`, the username itself and a repeated password are accepted for create, reset and change and can log in; empty and 129-character passwords are rejected. Not re-run in the UI after this change: the wizard/change-password windows were not re-driven with UI Automation. Risk assessment is recorded in `SECURITY.md`.

## "What the system protects against" (page + document)
* New app page **ระบบป้องกันอะไรได้บ้าง** and `docs/PROTECTION.md`, both derived from / checked against `ProtectionCatalog` (Client library). 13 topics: 4 covered, 4 partial, 5 not covered.
* **Claims are tested, not asserted**: `CatalogVerificationTests` sends every example as exact bytes over a real socket to an in-process gateway (with a fake upstream) and checks blocked/passed; checks that the rule sandbox agrees; IP block/allow semantics; the 429 rate limit; and that PROTECTION.md mentions every catalog topic. Test count is now 101. The in-app "ลองทดสอบ" buttons run the live sandbox (verified in the published exe: results read "บล็อก ✓ ตรงกับที่ระบุ").
* Findings made while writing it (documented, **not fixed**): URL-borne SQLi/XSS from real browsers mostly bypass because the rules see raw `%20` / `%3C` (only `sleep(` reliably matches in URLs); chunked, text/plain and multipart bodies, headers and cookies are not inspected; JSON/form bodies over 8,192 characters are blocked, harmless ones included (the effective limit is 8 KB, not the 64 KB the code suggests); `sleep(` in ordinary text is blocked; the rate limit is a fixed window. These motivate the v0.9 engine work in the roadmap.
* Not verified: HTTP/2, WebSocket, behaviour behind a load balancer, JSON `\uXXXX`-escaped payloads (not claimed).
* Process note: two background test runs were killed after a long hang (each pass-through request to a closed upstream port costs ~2 s on Windows); the suite now uses a local fake upstream. Leftover temporary databases were dropped, and the user's own `%AppData%\AegisWAF\gateway.settings` was backed up before the UI test and restored afterwards (checked).

## v0.7.1 — WAF engine upgrade ("maximum protection")
Goal: close the bypasses documented in PROTECTION.md (raw non-normalised matching; headers/cookies/non-JSON bodies not inspected; tiny signature set).
* **`Normalizer`** (Program.cs): per-field percent-decode up to 3× (defeats double/triple encoding), `+`→space, HTML-entity decode, SQL `/**/` comment strip, whitespace collapse, lowercase — produces a few canonical variants, tested in addition to the raw form. Never alters the forwarded request.
* **Inspection widened** (GatewayApp.cs): URL+query, each query value, a curated header set (User-Agent, Referer, Origin, X-Forwarded-*, Forwarded, …), all cookies, and bodies of every textual type including multipart and chunked (no Content-Length). Body cap raised to 128 KB and made configurable (`Waf:MaxBodyInspectBytes`), still fail-closed above it; the old 8 KB false-block on JSON/form bodies is gone.
* **Signatures**: 3 → 13 NonBacktracking rule groups (SQLi ×3, XSS ×3, path ×2, LFI/RFI, cmd ×2, SSRF, protocol anomaly) + a scanner-User-Agent rule. Linear-time (no ReDoS), each individually toggleable; `RuleText` descriptions and `ProtectionCatalog`/PROTECTION.md updated.
* **Tests**: 101 total. `CatalogVerificationTests` now proves each catalog claim on a real in-process gateway; the previously-bypassing examples assert BLOCKED.
* **Real-process probe** (published exe, throw-away DB, raw sockets) — before → after, now BLOCKED where they used to pass: `union%20select`, `or%201=1`, `union/**/select`, `'; DROP TABLE`, boolean-blind; `%3Cscript%3E`, encoded `<img onerror>`; `..%2f`, double-encoded `%252e`, `/etc/passwd`, `php://filter`; `;cat`, `|id`, `$(whoami)`; SSRF `169.254.169.254`; form/text/plain/multipart/chunked bodies; `<script>`/`sleep(5)`/`sqlmap` in User-Agent/Cookie/X-Forwarded-Host. Still pass (documented gaps): http-RFI, NoSQL `{"$ne":null}`; server-level 414/400 for oversized/malformed raw requests; normal and harmless-large traffic correctly pass.
* **Honest cost**: tuned for detection, so false positives rise (e.g. `sleep(` in ordinary prose is blocked; broad `(or|and) x=x` and shell-binary lists can hit legitimate data). Mitigation: every rule is toggleable in *กฎการป้องกัน*; IP allowlist bypasses.
* **Not done**: Unicode normalisation, response inspection, NoSQL/XXE/template injection, anomaly scoring, OWASP CRS — these remain v0.9. No independent pen-test. The new page/engine were not re-driven through the WPF UI after this change (the underlying calls are covered by tests; the page rendered correctly in the earlier UI-Automation run).

## v0.9 — remaining engine work (deep normalisation, new classes, scoring, response inspection, CRS study)
Goal: finish the detection items listed as "not done" after v0.7.1.
* **Deep Unicode normalisation** (`Normalizer`): added an NFKC compatibility fold (collapses fullwidth/compatibility code points such as `＜script＞` to ASCII) and zero-width/BOM stripping (defeats `un​ion`), as extra canonical variants alongside the existing decode/entity/comment passes. Never mutates the forwarded request.
* **New signature classes** (`SecurityRules`): NoSQL operator injection (`nosql-01`: `$ne/$gt/$where/…`, `user[$ne]=`), XXE (`xxe-01`: DOCTYPE/ENTITY, SYSTEM/PUBLIC to file/http), SSTI (`ssti-01`: `{{7*7}}`, `${…}`, `<%…%>`, `#{…}`, dangerous template keywords). 13 → 16 groups. Some payloads overlap earlier broader rules (a `file://` entity is attributed to LFI, `${…}` to shell) — still blocked, just under the earlier rule.
* **Anomaly scoring** (`AnomalyScorer`, rule `anomaly-01`): additive weak-signal scoring; blocks when one value's score ≥ threshold (default 7). **Off by default** (raises false positives); toggleable in the rules grid and via `Waf:AnomalyScoring:Enabled`/`Threshold`.
* **Response/leak inspection** (`ResponseInspector` + `ResponseBufferStream`): buffers textual upstream responses up to `Waf:MaxResponseInspectBytes` (256 KiB) and replaces the body with a 502 if it leaks a passwd/shadow file, private key, raw DB error, or stack trace. Responses that are binary or exceed the cap stream through unbuffered and are not inspected (documented fail-open). On by default (`Waf:InspectResponses`).
* **OWASP CRS**: a compatibility **study/decision record** (`OWASP_CRS_COMPATIBILITY.md`), not an import — CRS SecLang cannot load into .NET; decision is to adopt CRS *concepts* now (scoring, response rules, transform parity) and record a Coraza+CRS sidecar as the path to real CRS coverage once a multi-process deployment exists.
* **Deferred** from the v0.9 scope: rule exclusions (per-path/param allowlisting) and rule versioning.
* **Tests**: 101 → **125** (24 new in `EngineUpgradeTests`): Unicode fold/zero-width, each new signature class and its documented bypass, anomaly scoring on/off/threshold and the toggle/count, and five response-inspection integration tests over a scripted upstream (passwd leak → 502, DB-error leak → 502, clean HTML passes, binary fail-open, > cap streams through intact). `dotnet test`: **125 passed, 0 failed**.
* **Not done / not verified**: no independent pen-test; no benchmark against OWASP CRS on a shared corpus (that benchmark is itself a documented follow-up); the new rules/response path were not re-driven through the WPF UI (underlying calls are covered by tests).

## Verified results
| Check | Result |
|---|---|
| `dotnet build` | succeeded, 0 warnings, 0 errors |
| `dotnet test` | 53 passed, 0 failed (~18 s) |
| Real-process smoke test (Kestrel on 127.0.0.1, fresh DB) | GUI 200; unauthenticated API 401; login 200 with HttpOnly/SameSite=Strict cookie; authenticated API 200; `/health` 200 |
| `dotnet list package --vulnerable` | none found |
| Browser-level GUI test | **not done** – the login / user-management JavaScript was only syntax-checked (`node --check`) and exercised indirectly through the API |
| Load, performance, detection effectiveness | **not measured** |

## Weighted progress toward v1.0
Method: each workstream has a fixed list of v1.0 acceptance criteria. TESTED = 1, IMPLEMENTED but untested = 0.5, otherwise 0. Workstream score = earned / criteria; overall = Σ(weight × score).

| Workstream | Weight | Criteria | Earned | Score |
|---|---|---|---|---|
| Gateway | 15 | 8 | 2.0 | 25% |
| WAF Engine | 20 | 12 | 2.0 | 17% |
| Dashboard | 10 | 17 pages | 3.5 | 21% |
| Database | 10 | 10 | 2.5 | 25% |
| Authentication | 15 | 13 | 10.0 | 77% |
| Rule Management | 8 | 8 | 1.0 | 13% |
| API Security | 5 | 6 | 3.0 | 50% |
| Observability | 7 | 7 | 1.0 | 14% |
| High Availability | 5 | 6 | 0 | 0% |
| DevSecOps | 5 | 8 | 3.0 | 38% |
| **Overall (weighted)** | 100 | | | **≈ 30%** |

The criteria lists are judgement-based and will be rewritten per release; this is a tracking aid, not a readiness claim.

## Known limitations
See `SECURITY.md` (open findings 7–15 and the v0.7 limitations).

## Next actions
1. Dedicated admin listener + trusted-proxy handling.
2. Project split under the test suite.
3. Protected-application / per-site policy schema and API.
