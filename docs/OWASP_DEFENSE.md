# OWASP Top 10 defense coverage

AegisWAF exposes an A01-A10 coverage matrix through `/api/owasp-coverage` and the **OWASP Top 10** console view. Shield badges map implemented controls to OWASP categories; they are not a certification, guarantee, or substitute for secure application design, outbound allowlists, patching, or independent testing.

The catalog is the source of truth for category, coverage state, mapped rule IDs, descriptions, and limitations. `Partial` is intentional: signatures and normalization reduce common attack paths but cannot prove that an application is safe. Limitations remain visible next to each badge, and event metadata never includes request bodies.

## Additional defense layers

- SSRF protection parses URL tokens without DNS resolution, rejects dangerous schemes, loopback, private/link-local, metadata, mapped IPv4, and non-canonical IPv4 forms. DNS-based internal names still require an application outbound allowlist.
- Authentication failures are throttled per normalized IP and username with bounded in-memory state. Defaults are 10 failures per 15 minutes and 10,000 tracked keys; account lockout remains a separate defense.
- Admin-owned HTML/API/auth responses receive scoped security headers: CSP, `nosniff`, `no-referrer`, restrictive `Permissions-Policy`, and `no-store`. Upstream responses are not given the admin console policy.
