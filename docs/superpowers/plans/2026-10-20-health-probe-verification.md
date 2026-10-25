# Health probe and public prefix verification

Reproduced the 503 with a database-backed Site and an upstream that records every HTTP request. YARP actually requested `/login/login` because Active.Path was appended to the destination Address `/login/`. Prior changes to the slash alone did not fix this.

Changes:

- Set each destination Health address to its own origin, preserving the port, so Active.Path `/login` probes `/login` once.
- Remove the public route prefix `/donsak-i` rather than the upstream prefix `/login` before forwarding.
- Remove a non-root destination's trailing separator so the exact public prefix can reach canonical `/login` without a 308 redirect.
- Desktop version is 0.10.2.

Verification on 2026-10-20 (Asia/Bangkok):

- Regression initially failed with recorded probe `/login/login`.
- Real upstream opt-in test using AEGISWAF_PROBE_UPSTREAM=http://localhost:3000/login/ passed: Site API reports healthy; public `/donsak-i` returns 200 HTML with automatic redirects disabled.
- Focused regression tests: 44 passed, 0 failed.
- Release single-file publish completed to D:/AegisWAF/dist/AegisWAF-Desktop/AegisWAF.exe.

The real-upstream test uses a temporary Site in the test database and deletes it afterwards. It does not authenticate to the upstream application. Login submission, application assets, and application-generated root-relative navigation are not covered by this health/routing verification.

## Compressed response follow-up (v0.10.3)

Browser-style requests negotiate gzip. The middleware previously passed compressed bytes directly to the strict UTF-8 decoder and returned `Malformed Response UTF-8` / 502. The real-upstream regression was changed to negotiate compression and reproduced 502 before the fix.

Response inspection now decodes gzip, Brotli, and deflate with bounded input, expansion ratio, and output size, then scans the decoded content. The original wire representation and Content-Encoding header are preserved for successful responses. Malformed or unsupported encodings and oversized decompressed payloads fail closed; compressed private-key leaks remain blocked.

Verified: real Next.js upstream with compression enabled returns 200 HTML and healthy through the embedded Gateway; 53 focused tests passed. Release published v0.10.3, SHA-256 `6862B54458142DE3202EE74B1DBB732C13AEB9714B6D09FEF9D6670693A8EDA0`.
