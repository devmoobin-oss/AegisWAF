# Development Roadmap

Status labels: NOT STARTED / IN PROGRESS / IMPLEMENTED (code, untested) / TESTED (automated tests pass) / PRODUCTION VERIFIED (independent evidence complete).

| Version | Scope | Status |
|---|---|---|
| v0.7 | Authentication, RBAC, in-process admin API isolation, user/role schema, login + user-management GUI, auth audit, auth tests | **TESTED** (53 automated tests). MFA and per-IP login throttling deferred; GUI not browser-tested |
| v0.8 | Gateway project split, domain/path routing, per-site policy, admin isolation, health checks | **TESTED**; dedicated listener, trusted-proxy deployment and TLS termination remain infrastructure/deployment work |
| v0.7.1 | WAF engine upgrade: request normalisation (decode/entity/comment-strip), inspect headers+cookies+all body types, 13-rule signature set, detection regression suite (`CatalogVerificationTests`) | **TESTED** (pulled forward from v0.9) |
| v0.9 | Remaining engine work: anomaly scoring, Unicode (NFKC) normalisation, text-like opaque request/response inspection, response/leak inspection, NoSQL/XXE/SSTI signatures, OWASP CRS compatibility study | **TESTED** (engine, opaque-content, response and catalog regression tests; CRS study in `OWASP_CRS_COMPATIBILITY.md`). **Deferred:** rule exclusions (per-path/param allowlisting) and rule versioning |
| v0.10 | Structured security events, audit chain, readiness/health API, SIEM-compatible logging | **TESTED**; local bounded retention is implemented, while centralized delivery and alert routing remain deployment work |
| v0.11 | Shared distributed rate limiting/auth throttling, multi-site egress and health/failover | **TESTED** with PostgreSQL and real-socket local evidence; multi-instance external validation remains |
| v0.12 | Parser-aware hardening, bounded inspection, runtime egress enforcement, local security/load harness | **TESTED** locally; external load baseline and DNS-rebinding assessment remain |
| v0.13 | Threat model, authorised pen-test, hardening, dependency scanning | **IN PROGRESS**; dependency scan passed, external scanners/pen-test are pending |
| v1.0 | Production candidate — only when the evidence listed in [PRODUCTION_READINESS.md](PRODUCTION_READINESS.md) exists | **NOT STARTED** (external gates pending) |

## Next three tasks
1. Run independent multi-instance, DNS-rebinding and authorised penetration tests.
2. Run the new CI Security Verification workflow and remediate any CodeQL, Gitleaks or SBOM findings.
3. Establish an external load-test baseline, review capacity/latency results, and document operational SLOs.

Deferred from v0.9 (fold into a later engine pass): rule exclusions (per-path/param allowlisting to tame false positives before raising the anomaly-scoring default) and rule versioning. The Coraza+CRS sidecar prototype is gated on a multi-process deployment model (see `OWASP_CRS_COMPATIBILITY.md`).
