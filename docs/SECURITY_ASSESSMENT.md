# AegisWAF Security Assessment Runbook

Automated tests and load tests are evidence of implementation behavior, not independent certification. Run external assessment only against a synthetic or explicitly authorized environment.

## Automated gates

- Run the complete .NET test suite.
- Run `tools/security-loadtest/loadtest.ps1` against a local synthetic upstream.
- Run dependency vulnerability scan, secret scan, SAST, and SBOM generation where the tools are available.
- Preserve JSON/CSV outputs with commit/build hash and configuration checksum.

### Latest local harness evidence

On 2026-10-18, the harness was run against a local raw-TCP synthetic upstream with `-Requests 40 -Concurrency 4 -AttackRatioPercent 25 -HostHeader site-a.example`. The run completed with 40/40 HTTP 200 responses, 0 transport errors, 0 blocked responses, 42.707 requests/second, p50 1.258 ms, p95 102.181 ms, and p99 285.565 ms. This verifies the runner, explicit Host propagation, and machine-readable output path; it does not replace a multi-instance, external, or independent security assessment.

### Latest dependency/tooling evidence

On 2026-10-18, `dotnet list AegisWAF.slnx package --vulnerable --include-transitive` completed against `https://api.nuget.org/v3/index.json`; AegisWAF.Client, Gateway, Desktop, and Tests reported no vulnerable packages from that source. `gitleaks`, `semgrep`, `syft`, and `trivy` were not installed in the local environment, so secret scanning, SAST, and SBOM generation remain unverified rather than being reported as clean.

## OWASP ZAP / WSTG scope

Use an isolated deployment with a test database and non-production credentials. Cover:

- authentication, session expiry, CSRF, authorization and forced password change;
- request smuggling/framing, method restrictions, header normalization and protocol upgrade denial;
- SQLi, XSS, traversal, XXE, SSTI, NoSQL, command injection and SSRF payloads in query, path, headers, cookies, JSON, form, XML and multipart;
- decompression limits and oversized request/response behavior;
- structured parser differential cases, including duplicate JSON object keys;
- binary/content-type spoofing cases with bounded byte-safe response leak inspection enabled;
- upstream timeout/failure and egress policy enforcement;
- rate-limit behavior across multiple gateway instances;
- audit integrity, structured events, secrets in logs, and management-plane headers.

## Evidence record

For every finding record: target build SHA-256, configuration profile, test date/time, tool/version, request identifier, expected result, actual result, severity, reproduction, remediation status, and whether the result was independently verified.

Do not mark an OWASP category “fully covered” because one signature matched. Record parser coverage, known bypasses, false positives, and external-test status separately.
