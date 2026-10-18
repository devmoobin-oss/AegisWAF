# AegisWAF Security Assessment Runbook

Automated tests and load tests are evidence of implementation behavior, not independent certification. Run external assessment only against a synthetic or explicitly authorized environment.

## Automated gates

- Run the complete .NET test suite.
- Run `tools/security-loadtest/loadtest.ps1` against a local synthetic upstream.
- Run dependency vulnerability scan, secret scan, SAST, and SBOM generation where the tools are available.
- Preserve JSON/CSV outputs with commit/build hash and configuration checksum.

## OWASP ZAP / WSTG scope

Use an isolated deployment with a test database and non-production credentials. Cover:

- authentication, session expiry, CSRF, authorization and forced password change;
- request smuggling/framing, method restrictions, header normalization and protocol upgrade denial;
- SQLi, XSS, traversal, XXE, SSTI, NoSQL, command injection and SSRF payloads in query, path, headers, cookies, JSON, form, XML and multipart;
- decompression limits and oversized request/response behavior;
- upstream timeout/failure and egress policy enforcement;
- rate-limit behavior across multiple gateway instances;
- audit integrity, structured events, secrets in logs, and management-plane headers.

## Evidence record

For every finding record: target build SHA-256, configuration profile, test date/time, tool/version, request identifier, expected result, actual result, severity, reproduction, remediation status, and whether the result was independently verified.

Do not mark an OWASP category “fully covered” because one signature matched. Record parser coverage, known bypasses, false positives, and external-test status separately.
