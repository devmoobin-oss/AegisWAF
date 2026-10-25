# AegisWAF Security Notes (v0.7)

Status: **Production Candidate / not production-certified.** The Multi-Site worktree has local parser, egress, health/failover, distributed-control and real-socket evidence, but external penetration testing, independent load testing, DNS-rebinding assessment and continuous SAST/secret/SBOM scanning remain open gates. See [PRODUCTION_READINESS.md](PRODUCTION_READINESS.md) for the authoritative deployment gate.

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
| 7 | GUI/admin API and protected-app traffic share one listener; protected apps cannot use `/`, `/assets`, `/api` paths | High (design) | **Open** – dedicated admin listener planned v0.8 |
| 8 | Inspection worked on raw, non-normalised URL/query; headers/cookies and non-JSON/form bodies were not inspected | High | **Largely fixed (v0.7.1 + v0.9)** — `Normalizer` percent-decodes (incl. double encoding), `+`→space, HTML-entity-decodes, strips SQL comments, lowercases **and NFKC-folds Unicode / strips zero-width** before matching; headers, cookies and all textual body types (incl. multipart/chunked) are inspected; signature set expanded to 16 groups (+NoSQL/XXE/SSTI); an optional anomaly scorer and outbound **response-leak inspection** were added. Verified by `CatalogVerificationTests` + `EngineUpgradeTests`. Remaining gaps are documented in `PROTECTION.md` (MySQL versioned comments, http-RFI, unlisted headers, octet-stream request/response bodies, responses > 256 KiB, type-juggling NoSQL). **New risk:** the aggressive rules raise the false-positive rate (e.g. `sleep(` in ordinary text); rules are individually toggleable and the anomaly scorer is off by default |
| 9 | Explicit IP "allow" bypasses all inspection and rate limiting | Medium | **Open** (v0.4 design; revisit) |
| 10 | Client IP comes from the socket only; behind a load balancer all clients share one IP (rate limit, blocklist); no trusted-proxy model | High | **Open** – v0.8 / v0.11 |
| 11 | Rate limiter is a fixed window (named "sliding"), hard-coded 300/min, per process | Medium | **Open** – v0.11 |
| 12 | Event writer opens a connection per event; no batching, retention or partitioning; counters are in memory only | Medium | **Open** – v0.10 |
| 13 | No TLS termination/management; session cookie is `Secure` only when the request itself is HTTPS | High | **Open** – v0.8; if `Admin:AllowRemote` is ever enabled, put HTTPS in front |
| 14 | Dev DB account is the `postgres` superuser | Medium | **Open** – create a least-privilege role for production |
| 15 | Audit log is append-only by convention, not tamper-evident | Low | **Open** |

## Current password policy
The current implementation requires passwords to be 12–128 characters, contain upper/lower-case characters, a digit and a symbol, and not contain the username. Passwords use PBKDF2-HMAC-SHA512 with 210,000 iterations. Production additionally requires distributed PostgreSQL authentication throttling and bounded lockout/session settings. MFA/WebAuthn remains intentionally out of scope for this deployment.

## v0.7 limitations (known)
* No MFA (column reserved) and no email-based self-service reset; reset is administrator-initiated and the user must change the password at next login.
* Login attempts are throttled per account only (lockout), not per source IP. Because the admin API is loopback-only by default, only a local user could use lockout to deny an account.
* CSRF relies on the synchroniser token plus `SameSite=Strict`; there is no `Origin` check.
* Session lookups hit PostgreSQL on every admin request (fine at admin request rates; add caching before scaling).
* The session cookie is not `__Host-` prefixed because plain-HTTP loopback use is supported.

## Secrets
* No credentials are in source. `appsettings.json` has the connection string **without a password**; supply `ConnectionStrings__Postgres` through the environment or user-secrets.
* Optional `appsettings.local.json` (see README) stores the DB password and optionally the bootstrap password **in clear text on disk**; it is a convenience for single-user Windows machines, not a secrets manager. Restrict file permissions and remove the bootstrap password after first login.
* The bootstrap password is read from the environment (or `Bootstrap:AdminPassword`), never logged, and used only when no users exist. Change it after first login.
* Dependency scan (2026-10-19, `dotnet list package --vulnerable --include-transitive`): no known vulnerable packages from the configured NuGet source. A repeatable CI workflow now runs dependency audit, CodeQL, Gitleaks and SBOM generation; the workflow itself still requires a successful hosted run before its evidence is considered complete.
* Parser-aware inspection, bounded decompression, explicit upstream egress policy, shared authentication throttling, and a local security/load-test harness were added in the 2026-10-18 production-elevation work. These controls still require independent penetration testing and multi-instance operational validation.

## Testing policy
All tests use synthetic requests against a throw-away database. Security fixes must ship with a regression test (see `tests/AegisWAF.Tests`).
