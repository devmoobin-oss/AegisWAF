# AegisWAF Architecture (v0.7)

## Decision: keep the single-project layout for now
The v0.6 code base is one ASP.NET Core project (`src/AegisWAF.Gateway`). The master plan describes separate Gateway / SecurityEngine / AdminAPI / Infrastructure / Dashboard projects. Splitting now would be a large mechanical change with no behavioural benefit and no tests to guard it. v0.7 therefore adds **namespaces and folders** (`Auth/`, `Infrastructure/`) inside the existing project, and the split is scheduled for v0.8 together with the dedicated admin listener, once there is a test suite to protect the refactor.

## Components (current)
| Area | Location | Notes |
|---|---|---|
| Reverse proxy | YARP, `appsettings.json` `ReverseProxy` | Static single route/cluster; no per-site config yet |
| WAF inspection | `Program.cs` (`Normalizer`, `SecurityRules`, `AnomalyScorer`, `ResponseInspector`) + inspection middleware in `GatewayApp.cs` | **v0.9:** `Normalizer` canonicalises each field (percent-decode incl. double-encoding, `+`→space, HTML-entity decode, SQL `/**/` strip, whitespace collapse, lowercase, **NFKC Unicode fold + zero-width stripping**) before matching. 16 NonBacktracking rule groups (SQLi×3, XSS×3, path×2, LFI/RFI, cmd×2, SSRF, protocol anomaly, **NoSQL, XXE, SSTI**) + a scanner User-Agent rule + an **optional additive anomaly scorer** (`anomaly-01`, off by default, `Waf:AnomalyScoring:*`) + custom regex rules. Inspects URL/query, selected headers, cookies, and request bodies of all textual types up to `Waf:MaxBodyInspectBytes` (default 128 KiB, fail-closed above), **and the upstream response** (`ResponseInspector`: buffers ≤ `Waf:MaxResponseInspectBytes` = 256 KiB, suppresses passwd/key/SQL-error/stack-trace leaks with a 502; larger or binary responses stream through = fail-open). Tuned for detection → higher false-positive rate; every rule toggleable. Coverage verified by `CatalogVerificationTests` + `EngineUpgradeTests`; see `PROTECTION.md` and `OWASP_CRS_COMPATIBILITY.md` |
| Rate limiting / IP policy | `Program.cs` | In-process; fixed 60 s window, 300 req/IP; exact-match IP allow/block |
| Event pipeline | `SecurityEvents` + `EventWriter` | Bounded channel (10 000), drop counter; one DB connection per event (needs batching) |
| **Auth / RBAC (v0.7)** | `Auth/` | See below |
| **Migrations (v0.7)** | `Infrastructure/Migrator.cs`, `database/*.sql` | SQL embedded in the assembly; tracked in `schema_migrations` with SHA-256 checksums; advisory-lock serialised |
| **Desktop app** | `src/AegisWAF.Desktop` (WPF, net10.0-windows) + `src/AegisWAF.Client` | Native Windows client. `AegisWAF.Client` (plain net10.0 library, covered by integration tests) wraps the admin API: session cookie in a cookie container, CSRF token in memory, `SessionExpired` event. The WPF app only renders and calls this client; it never touches the database. The gateway is hosted **in-process**: `GatewayApp` (CreateBuilder / AddServices / MapPipeline, shared with the standalone `Program.cs`) is driven by `EmbeddedGateway`, which starts Kestrel on thread-pool threads, applies migrations/bootstrap first and fails with an exception (DB down, port in use) instead of half-starting. Desktop settings are stored under `%AppData%` with Windows DPAPI (`SettingsStore`). |
| Web dashboard | `wwwroot/` | Static HTML/JS (no framework); login overlay and user management added in v0.7 |

## Request pipeline order (`Program.cs`)
1. Security-header middleware + GUI network guard (`/`, `/index.html`, `/assets` local only)
2. `UseDefaultFiles` / `UseStaticFiles` — **must precede routing**: static files yield to any matched endpoint and the YARP catch-all matches everything. v0.6 had this wrong, so the dashboard was proxied upstream instead of served.
3. `UseRouting`
4. `AdminGuard` for `/api/*`: local-network check → unknown route 404 → route without `AdminAccess` metadata 403 (closes the catch-all bypass) → session → must-change-password gate → role level → CSRF (non-GET)
5. WAF inspection middleware (skips `/api`, `/health`, GUI paths): inspects the request, then — for proxied paths — swaps `Response.Body` for a `ResponseBufferStream` so the upstream response can be scanned for leaks before the client sees it (large/binary responses overflow the buffer and stream through unmodified)
6. Endpoints / YARP proxy

## Authentication & RBAC design
* **Passwords**: PBKDF2-HMAC-SHA512, 210 000 iterations, 16-byte salt, self-describing format (`pbkdf2-sha512$iter$salt$hash`) so parameters can be raised later. Policy: **none by operator decision** — any non-empty password up to 128 characters (the cap only bounds hashing cost). Unknown users trigger a dummy hash to equalise timing.
* **Sessions**: 256-bit random token in an `HttpOnly; SameSite=Strict` cookie (`Secure` whenever the request is HTTPS). Only the SHA-256 of the token is stored. Idle timeout 30 min, absolute 8 h, configurable (`Auth:*`). Each login creates a new session (no fixation).
* **CSRF**: per-session token returned by login/session, required in `X-CSRF-Token` for every non-GET `/api` call, compared in constant time, held in JS memory only.
* **Lockout**: 5 consecutive failures lock the account for 15 min (atomic SQL update). Locked, disabled and unknown accounts return the same generic 401.
* **Roles** (`roles.level`): VIEWER 1 < SECURITY_ANALYST 2 < SECURITY_OPERATOR 3 < SECURITY_ADMIN 4 < SUPER_ADMIN 5. Each endpoint declares a minimum level via `.Require(level)`. The role is read from the database on every request, so role changes and disablement take effect immediately. Endpoints without metadata are denied.
* **Last-admin protection**: demoting or disabling the last enabled SUPER_ADMIN is rejected under an advisory lock.
* **Bootstrap**: when `users` is empty, `AEGISWAF_BOOTSTRAP_ADMIN_PASSWORD` (and optional `AEGISWAF_BOOTSTRAP_ADMIN_USER`, default `admin`) creates the first SUPER_ADMIN. Nothing is created if the variable is unset or violates the policy.
* **MFA-ready**: `users.mfa_secret` is reserved; no TOTP logic yet.

## Authorization matrix
| Operation | Min role |
|---|---|
| Overview, rule list, custom-rule list, IP list, proxy inventory | VIEWER |
| Audit log, rule sandbox | SECURITY_ANALYST |
| Add/remove IP policy | SECURITY_OPERATOR |
| Toggle rules, create/delete custom rules | SECURITY_ADMIN |
| User CRUD, password reset, session revocation | SUPER_ADMIN |
| Own session, logout, change own password | any authenticated user (also while a password change is pending) |

## Database (v0.7 additions, `006_auth_rbac.sql`)
`roles`, `users`, `user_roles`, `sessions` (FKs, CHECK on username, partial index on live sessions); `waf_audit` gains `actor`, `client_ip`, `outcome`. The existing `waf_audit` table is kept as the audit store instead of adding a second `audit_logs` table.

## Target architecture (not yet built)
Dedicated admin listener → project split → per-site policies (v0.8) → normalising rule engine with anomaly scoring (v0.9) → batched/partitioned event store, SignalR, SIEM (v0.10) → Redis (v0.11). See `DEVELOPMENT_ROADMAP.md`.
