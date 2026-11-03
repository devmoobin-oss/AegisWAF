# AegisWAF Security Notes (v0.7)

> **Superseded since v0.14:** the username/password login system described throughout this document (sessions, RBAC, password policy, account lockout, login-abuse throttling) has been **removed**. Administration is now restricted by network perimeter only: the admin API and dashboard are local-host-only unless `Admin:AllowRemote=true`, in which case HTTPS is required and **any** client reaching the endpoint is treated as authorized — there is no credential check. See [AdminGuard.cs](../src/AegisWAF.Gateway/Auth/AdminGuard.cs) and the README's Access model section. The findings and policy below (table row 1, "Password policy and legacy hashes", and the login-related bullets under "v0.7 limitations") describe the now-removed system and are kept only as historical record of what v0.7 fixed.

Status: **Production Candidate / not production-certified.** The Multi-Site worktree has local parser, egress, health/failover, distributed-control and real-socket evidence; Hosted run `40445541235` also passed the 509/509 regression, dependency audit, secret scan, and SBOM gates. CodeQL/SARIF repository enablement, external penetration testing, independent load testing, DNS-rebinding assessment, and operational SIEM validation remain open gates. See [PRODUCTION_READINESS.md](PRODUCTION_READINESS.md) for the authoritative deployment gate.

The v0.7 findings below are historical baseline findings. Where later Production-elevation work superseded a finding, the current implementation and readiness document take precedence.

See also [PROTECTION.md](PROTECTION.md) for exactly which attack classes the gateway does and does not stop (each claim is verified by automated tests).

## Baseline audit findings (v0.6) and status
| # | Finding | Severity | Status in v0.7 |
|---|---|---|---|
| 1 | Single shared admin token, stored in browser `sessionStorage`; no user identity in audit | High | **Fixed** – per-user login, HttpOnly session cookie, RBAC, audit with actor |
| 2 | Unmatched `/api/*` paths skipped inspection and could be forwarded to the upstream by the YARP catch-all | High | **Fixed** – `/api` is deny-by-default (404/403); regression test |
| 3 | Admin network check used `LocalIpAddress` (listener address), not the client address | Medium | **Fixed** – checks client `RemoteIpAddress` (loopback only unless `Admin:AllowRemote=true`) |
| 4 | Dashboard unreachable: static files shadowed by the proxy catch-all (found by running the app) | Medium | **Fixed** – middleware order; regression test |
| 5 | IP-policy delete was not audited; audit rows had no actor | Medium | **Fixed** |
| 6 | No migration tracking; scripts applied by hand | Medium | **Fixed** – `schema_migrations` with checksums |
| 7 | GUI/admin API and protected-app traffic share one listener; protected apps cannot use `/`, `/assets`, `/api` paths | High (design) | **Improved** – optional dual-listener path isolation is enforced when `Admin:ListenerPort` and `Gateway:ListenerPort` are configured; separate processes remain an infrastructure option |
| 8 | Inspection worked on raw, non-normalised URL/query; headers/cookies and non-JSON/form bodies were not inspected | High | **Largely fixed (v0.7.1 + v0.9 + opaque-content hardening)** — `Normalizer` percent-decodes (incl. double encoding), `+`→space, HTML-entity-decodes, strips SQL comments, lowercases **and NFKC-folds Unicode / strips zero-width** before matching; headers, cookies, textual bodies, text-like opaque bodies and multipart/chunked requests are inspected; signature set expanded to 16 groups (+NoSQL/XXE/SSTI); optional anomaly scoring and outbound **response-leak inspection** were added. Verified by `CatalogVerificationTests` + `EngineUpgradeTests`. Remaining gaps are documented in `PROTECTION.md` (MySQL versioned comments, http-RFI, unlisted headers, responses > 256 KiB, type-juggling NoSQL, and genuinely binary/invalid-UTF8 content). **New risk:** the aggressive rules raise the false-positive rate (e.g. `sleep(` in ordinary text); rules are individually toggleable and the anomaly scorer is off by default |
| 9 | Explicit IP "allow" bypasses all inspection and rate limiting | Medium | **Fixed** – allow is blocklist-only; inspection and rate limiting still run; exact IP and CIDR policies use most-specific matching |
| 10 | Client IP comes from the socket only; behind a load balancer all clients share one IP (rate limit, blocklist); no trusted-proxy model | High | **Improved** – client address and forwarded scheme/host honor only explicitly trusted direct peers; production still requires correct proxy CIDRs |
| 11 | Rate limiter is a fixed window (named "sliding"), hard-coded 300/min, per process | Medium | **Improved** – configurable rolling-window ceilings with optional global/shared PostgreSQL enforcement |
| 12 | Security-event storage needs bounded retention/partitioning and durable external delivery; batching, local retention and delivery counters are implemented | Medium | **Open** – external SIEM/partitioning |
| 13 | No TLS termination/management; session cookie is `Secure` only when the request itself is HTTPS | High | **Improved** – trusted forwarded HTTPS is recognized for admin enforcement and cookie flags; certificate lifecycle is still infrastructure-owned |
| 14 | Dev DB account is the `postgres` superuser | Medium | **Improved** – Production enforcement requires `Database:RequireLeastPrivilege=true` and fails closed when `current_user` is a PostgreSQL superuser; Desktop/dev defaults remain compatibility-oriented, so production role provisioning is still an operational gate |
| 15 | Audit log is append-only by convention, not tamper-evident | Low | **Improved** – SHA-256 `prev_hash`/`entry_hash` chain, `/api/audit/integrity`, and a deduplicated critical integrity-failure alert are implemented; external SIEM delivery/retention remains open |

## Password policy and legacy hashes
New password creation and changes enforce **at least 12 characters**, upper/lower/digit/symbol classes, username exclusion, and a 128-character maximum. Legacy hashes created under the older policy may still be weak and should be rotated. Consequences to be aware of:
* Password creation/change currently requires at least 12 characters plus upper/lower/digit/symbol classes and rejects the username. Login abuse is bounded by account lockout plus local IP/username/pair buckets; production uses the shared PostgreSQL IP/username throttle.
* Newly created or changed accounts use the current password policy, but legacy hashes created under older policy may still represent weak passwords and should be rotated by administrators.
* Recommended compensating habits: keep `Admin:AllowRemote` off, do not expose the gateway's admin port, rotate legacy weak hashes, and give SUPER_ADMIN / SECURITY_ADMIN accounts unique passwords.
Password hashing, lockout, session handling and the forced change of administrator-issued temporary passwords are unchanged.

## v0.7 limitations (known)
* No MFA (column reserved) and no email-based self-service reset; reset is administrator-initiated and the user must change the password at next login.
* Login attempts are bounded by account lockout plus local IP/username/pair buckets; production deployments must use the shared PostgreSQL IP/username throttle. MFA/WebAuthn/OIDC remains a separate hardening gap.
* CSRF uses the synchroniser token, `SameSite=Strict`, and effective-origin validation; the effective host/scheme are proxy-aware only for explicitly trusted peers.
* Session lookups hit PostgreSQL on every admin request (fine at admin request rates; add caching before scaling).
* The session cookie is not `__Host-` prefixed because plain-HTTP loopback use is supported.

## Secrets
* No credentials are in source. `appsettings.json` has the connection string **without a password**; supply `ConnectionStrings__Postgres` through the environment or user-secrets.
* Optional `appsettings.local.json` (see README) stores the DB password **in clear text on disk**; it is a convenience for single-user Windows machines, not a secrets manager. Restrict file permissions.
* There is no bootstrap admin password: the login system was removed and administration is restricted by network perimeter only (see the superseded notice above).
* Dependency scan (2026-10-17, `dotnet list package --vulnerable --include-transitive`): no known vulnerable packages. This is a one-off check; continuous scanning in CI is not yet configured.
* Parser-aware inspection, bounded decompression, explicit upstream egress policy, shared authentication throttling, audit-chain integrity alerting, and a local security/load-test harness were added in the 2026-10-18 production-elevation work. These controls still require independent penetration testing, SIEM delivery/retention validation, and multi-instance operational validation.
* Hosted Security Verification run `40445541235` (2026-10-27): the 509/509 regression, dependency audit, Gitleaks secret scan, SBOM generation, and direct-origin guardrail passed; CodeQL remained fail-closed at the repository Code scanning/Advanced Security preflight and is not claimed as passed.
* Parser-aware inspection, bounded decompression, explicit upstream egress policy, shared authentication throttling, audit-chain integrity alerting, and a local security/load-test harness were added in the 2026-10-18 production-elevation work. These controls still require independent penetration testing, SIEM delivery/retention validation, and multi-instance operational validation.

## Testing policy
All tests use synthetic requests against a throw-away database. Security fixes must ship with a regression test (see `tests/AegisWAF.Tests`).
