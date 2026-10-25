# Development Roadmap

Status labels: NOT STARTED · IN PROGRESS · IMPLEMENTED (code, untested) · TESTED (automated tests pass) · PRODUCTION VERIFIED (nothing is at this level yet).

| Version | Scope | Status |
|---|---|---|
| v0.7 | Authentication, RBAC, in-process admin API isolation, user/role schema, login + user-management GUI, auth audit, auth tests | **TESTED** (automated tests). Local/shared IP and username login throttling plus CIDR-aware IP policies implemented; MFA deferred; GUI not browser-tested |
| v0.8 | Dedicated admin listener, project split, protected-application CRUD, domain routing, per-site policy, trusted proxies, TLS config, health checks | NOT STARTED |
| v0.7.1 | WAF engine upgrade: request normalisation (decode/entity/comment-strip), inspect headers+cookies+all body types, 13-rule signature set, detection regression suite (`CatalogVerificationTests`) | **TESTED** (pulled forward from v0.9) |
| v0.9 | Remaining engine work: anomaly scoring, Unicode (NFKC) normalisation, response/leak inspection, NoSQL/XXE/SSTI signatures, OWASP CRS compatibility study | **TESTED** (24 new tests in `EngineUpgradeTests`; CRS study in `OWASP_CRS_COMPATIBILITY.md`). **Deferred:** rule exclusions (per-path/param allowlisting) and rule versioning |
| v0.10 | Batched + partitioned event store, retention, SignalR, analytics API, SIEM outputs, alerting | NOT STARTED |
| v0.11 | Redis shared state, distributed rate limiting, config sync | NOT STARTED |
| v0.12 | Load testing against a no-WAF baseline, profiling, tuning | NOT STARTED |
| v0.13 | Threat model, authorised pen-test, hardening, dependency scanning in CI | NOT STARTED |
| v1.0 | Production candidate – only when the evidence listed in the master plan (section 19) exists | NOT STARTED |

## Next three tasks
1. Dedicated admin listener + trusted-proxy model (closes `SECURITY.md` findings 7 and 10).
2. Split the solution into Gateway / SecurityEngine / Infrastructure projects under the existing test suite.
3. Protected-application CRUD and per-site policy tables (`protected_applications`, `upstream_servers`, `waf_policies`).

Deferred from v0.9 (fold into a later engine pass): rule exclusions (per-path/param allowlisting to tame false positives before raising the anomaly-scoring default) and rule versioning. The Coraza+CRS sidecar prototype is gated on a multi-process deployment model (see `OWASP_CRS_COMPATIBILITY.md`).
