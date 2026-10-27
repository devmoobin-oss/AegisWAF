# AegisWAF Security Assessment Runbook

Automated tests and load tests are evidence of implementation behavior, not independent certification. Run external assessment only against a synthetic or explicitly authorized environment.

Multi-Site `WafPolicy` resolves to an immutable per-request snapshot when `Waf:Policies:<name>` is configured. External testing must verify per-Site deny-only rule isolation, while treating persistent policy storage and live reload as separate open gates.

## Automated gates

- Run the complete .NET test suite.
- Run `tools/security-loadtest/loadtest.ps1` against a local synthetic upstream.
- Run `tools/security-assessment/run-assessment.ps1` to collect repeatable test, dependency, secret, SAST, SBOM, and authorized load-test status; unavailable tools and omitted targets remain non-passing statuses.
- Run dependency vulnerability scan, secret scan, SAST, and SBOM generation where the tools are available.
- Preserve JSON/CSV outputs with commit/build hash and configuration checksum.

### Latest local harness evidence

On 2026-10-19, the harness was run against a local raw-TCP synthetic upstream at `http://127.0.0.1:5080`. The run completed with 82 requests, 78 blocked responses (403), 4 clean proxied responses (200), 0 transport errors, 15.6 requests/second, p50 0.982 ms, p95 153.162 ms, and p99 2147.144 ms. This verifies runtime blocking/proxy behavior and machine-readable output; it does not replace a multi-instance, external, or independent security assessment.

A second 15-second run against the Gateway with the configured upstream intentionally unavailable completed with 92 requests, 76 WAF blocks (403), 16 upstream failures (502), 0 client errors, and machine-readable JSON/CSV output under `tools/security-assessment/artifacts/latest-release/`. This confirms fail-safe deny behavior under concurrent synthetic traffic; it is not a healthy-upstream performance baseline.

### Latest Hosted Security Verification evidence

On 2026-10-27, Hosted Security Verification run `40445541235` evaluated the Module 78 runtime change at commit `555749855f77b891c62914c5a26b9ced61e6ddeb`. The full .NET regression evidence was **509/509 passed, 0 failed, 0 skipped**, with `TRX_COUNTERS total=509 executed=509 passed=509 failed=0 skipped=0` and `TRX_REGRESSION_PASS`. Dependency vulnerability audit, SBOM generation, secret scan, and the direct-origin exposure guardrail also passed.

The Hosted CodeQL stage did not pass: its preflight failed closed because repository Code scanning/Advanced Security was unavailable, so CodeQL initialize/build/analyze and SARIF upload were skipped. This is not a CodeQL pass; Module 41 and Module 60 remain at 90% until the repository setting is enabled and a fresh SARIF result is verified.

### Historical local dependency/tooling evidence

On 2026-10-19, the local Release assessment runner completed `dotnet test --configuration Release` with **328 passed, 0 failed, 0 skipped**. This is retained as historical local evidence, not the latest regression total. `dotnet list AegisWAF.slnx package --vulnerable --include-transitive` completed against `https://api.nuget.org/v3/index.json`; AegisWAF.Client, Gateway, Desktop, and Tests reported no vulnerable packages from that source. The real-socket Multi-Site/distributed-control filter reported **15 passed**. `gitleaks`, `semgrep`, `syft`, and `trivy` were not installed in that local environment, so local secret scanning, SAST, and SBOM generation remain unverified rather than being reported as clean.

## OWASP ZAP / WSTG scope

Use an isolated deployment with a test database and non-production credentials. Cover:

- authentication, session expiry, CSRF, authorization and forced password change;
- request smuggling/framing, method restrictions, header normalization and protocol upgrade denial;
- SQLi, XSS, traversal, XXE, SSTI, NoSQL, command injection and SSRF payloads in query, path, headers, cookies, JSON, form, XML and multipart;
- decompression limits and oversized request/response behavior;
- structured parser differential cases, including duplicate JSON object keys;
- binary/content-type spoofing cases with bounded byte-safe response leak inspection enabled;
- binary-labeled request payloads with bounded byte-safe raw inspection enabled;
- upstream timeout/failure and egress policy enforcement;
- rate-limit behavior across multiple gateway instances;
- audit integrity, structured events, secrets in logs, and management-plane headers.

## Evidence record

For every finding record: target build SHA-256, configuration profile, test date/time, tool/version, request identifier, expected result, actual result, severity, reproduction, remediation status, and whether the result was independently verified.

Do not mark an OWASP category “fully covered” because one signature matched. Record parser coverage, known bypasses, false positives, and external-test status separately.
