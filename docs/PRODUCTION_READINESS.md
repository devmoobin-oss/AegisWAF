# AegisWAF Production Readiness

## Runtime baseline enabled

- Admin is local-only by default; remote administration requires HTTPS.
- `X-Forwarded-For` is trusted only when the direct peer matches `Security:TrustedProxies`.
- IP `allow` is an allowlist decision only; it does not bypass signatures or rate limiting.
- Rate limiting uses a rolling window and is configurable under `Waf:RateLimit`.
- Kestrel limits request line, headers, body size, keep-alive and header timeout.
- CSRF tokens and Origin validation protect state-changing admin requests.
- Passwords require at least 12 characters, upper/lower case, a digit and a symbol, and may not contain the username.
- Ambiguous request framing is rejected: duplicate `Content-Length`, `Content-Length` with `Transfer-Encoding`, and unsupported transfer encodings.
- New audit rows use `prev_hash` and `entry_hash` SHA-256 chaining.
- Analysts can query `/api/audit/integrity` to detect a broken chain.
- `/health` is liveness-only; `/ready` verifies PostgreSQL connectivity before traffic should be admitted.
- `/ready` also verifies every enabled explicit Site has a YARP cluster with at least one destination that is not marked unhealthy by either active or passive health checks; unknown destinations remain eligible during initial probing.
- Compressed request bodies and protocol upgrades are denied by default because the WAF cannot inspect them safely; enable `Waf:AllowProtocolUpgrade` only for a dedicated, tested route.
- An opt-in fail-closed deployment gate is available with `Waf:Deployment:Enforce=true`; it rejects auto-migration, superuser database roles, unsafe methods, unsafe upgrades, missing rate-limit bounds, and wildcard hosts for remote administration.
- SSRF filtering blocks loopback, RFC1918/link-local, metadata hostnames, IPv4 CGNAT, IPv4 multicast/reserved ranges, IPv6 Unique Local and IPv6 multicast targets, including encoded and non-canonical IPv4 forms.
- Response inspection can fail closed for oversized textual responses with `Waf:ResponseOverflowAction=block`; the default Desktop compatibility profile remains `stream` and must be changed for a strict Production deployment.
- Admin/control-plane responses send `no-store`, `X-Frame-Options: DENY`, COOP/CORP, `X-Permitted-Cross-Domain-Policies: none`, CSP and restrictive Permissions-Policy headers.
- Rate limiting supports both per-client and global rolling-window ceilings. Production uses the PostgreSQL shared store so multiple WAF instances enforce one common limit; run migration `008_rate_limit_buckets.sql` before enabling traffic.
- `/ready` verifies the shared rate-limit table exists when `Store=postgres`; traffic should not be admitted until this migration check is healthy.
- Security events are emitted as structured `aegiswaf.security_event` JSON logs with client, request, status and OWASP mapping fields, allowing a SIEM/agent to ingest events even when database persistence is temporarily unavailable.
- Structured events include the originating `requestId`, enabling correlation across WAF, SIEM and upstream traces without changing the existing database schema.
- If the bounded event queue fills, the WAF emits a throttled `WAF_SECURITY_EVENT_DROPPED` alert with the event payload and counter so SIEM can detect observability loss.
- Production-enforced sessions always set the cookie `Secure` flag, including deployments behind a TLS-terminating reverse proxy.
- If the shared PostgreSQL limiter is unavailable, request admission fails closed instead of bypassing rate limiting or returning an unhandled middleware error.
- The shared limiter opens a short fail-closed circuit after repeated store failures, reducing connection storms while PostgreSQL is unavailable.
- Production enforcement enables DNS-aware SSRF checks (`Waf:Ssrf:ResolveDns=true`) with bounded resolution, short-lived caching and fail-closed behavior for private/link-local resolutions or DNS failure; network egress policy is still required at the infrastructure layer.
- DNS decisions are bounded to 2,048 cached hosts with expiry cleanup, preventing hostname-cardinality memory exhaustion.
- When Production enforcement is enabled, every reverse-proxy destination must be an absolute HTTP(S) URL without embedded credentials, preventing malformed or secret-bearing upstream configuration from starting.
- The local limiter fails closed when its identity bucket table is full and preserves active buckets instead of evicting them randomly, reducing bucket-exhaustion abuse; multi-instance deployments still require a shared limiter.
- Expired per-IP and global limiter buckets are cleaned periodically, preventing long-running processes from retaining stale identity state indefinitely.
- Production enforcement bounds inspection buffers to 16 MiB for request bodies and 32 MiB for responses, preventing unsafe memory settings from creating a configuration-driven exhaustion risk.
- Structured request inspection now extracts bounded JSON, form-urlencoded, XML, and multipart fields/metadata before rule evaluation; ordinary HTML DOCTYPE is allowed while entity/external-DTD payloads are rejected.
- gzip, deflate, and Brotli request bodies are decompressed only within compressed-size, decompressed-size, expansion-ratio, and parser budgets; unknown encodings fail closed.
- Production upstreams require explicit HTTPS host/port/scheme allowlists and are checked against DNS-resolved private, loopback, metadata, multicast, reserved, and link-local targets before proxying.
- Generated Multi-Site YARP clusters enable active `/health` probes and passive failure tracking; destination selection uses `HealthyAndUnknown` so unhealthy upstreams are removed while unknown destinations remain eligible until the first probe completes.
- `/api/sites` projects runtime YARP destination health, so the Desktop view no longer treats an unprobed destination as healthy by default.
- Generated Multi-Site clusters explicitly enable YARP load balancing with `PowerOfTwoChoices`; configured upstream weights remain part of the Site model, while YARP health state controls availability.
- Under `Waf:Deployment:Enforce=true`, YARP's outbound `SocketsHttpHandler` connection callback rechecks the exact destination host/port and current DNS answer immediately before opening a socket, then connects to the validated address rather than resolving again after validation. This is defense-in-depth and does not replace infrastructure egress ACLs.
- Production startup rejects `DangerousAcceptAnyServerCertificate` and the egress classifier rejects special-use IPv4 ranges including unspecified, documentation, benchmark, and deprecated relay ranges.
- Production authentication throttling uses shared PostgreSQL state for normalized source and username keys; the WAF limiter also includes bounded route keys and the readiness check verifies the distributed authentication migration.
- A safe local security/load-test harness is available at `tools/security-loadtest/loadtest.ps1`; it emits JSON/CSV latency, throughput, status, block, and error evidence without destructive payloads.
- Multi-Site routing is configured under `Sites`; each Site has explicit hostnames, path prefixes, upstreams, rate limits and egress policy. Unknown hosts fail closed when Production enforcement is enabled.
- Site-scoped rate-limit keys include SiteId and route, and structured security events include SiteId, hostname and upstream context. Run migration `010_site_event_context.sql` before enabling event persistence for Multi-Site.

## Required deployment configuration

```json
{
  "Admin": { "AllowRemote": false, "RequireHttps": true },
  "Security": { "TrustedProxies": ["10.0.0.0/8"] },
  "Waf": {
    "Deployment": { "Enforce": true },
    "ResponseOverflowAction": "block",
    "RateLimit": { "Requests": 300, "GlobalRequests": 10000, "Store": "postgres", "WindowSeconds": 60, "MaxEntries": 10000 },
    "Limits": { "MaxRequestBodyBytes": 1048576 }
  },
  "Database": { "RequireLeastPrivilege": true, "AutoMigrate": false },
  "AllowedHosts": "waf.example.com"
}
```

Never configure `0.0.0.0/0` as a trusted proxy. Only infrastructure proxies belong in this list.

### Multi-Site example

```json
{
  "Sites": {
    "site-a": {
      "Enabled": true,
      "Hostnames": ["app-a.example.com"],
      "PathPrefixes": ["/"],
      "Upstreams": [{ "Id": "primary", "Address": "https://origin-a.example.com/", "Weight": 100 }],
      "WafPolicy": "strict",
      "RateLimit": { "Requests": 300, "WindowSeconds": 60, "GlobalRequests": 10000 },
      "Egress": { "AllowedHosts": ["origin-a.example.com"], "AllowedPorts": [443], "AllowedSchemes": ["https"], "AllowPrivateExceptions": false }
    },
    "site-b": {
      "Enabled": true,
      "Hostnames": ["app-b.example.com"],
      "PathPrefixes": ["/"],
      "Upstreams": [{ "Id": "primary", "Address": "https://origin-b.example.com/", "Weight": 100 }],
      "WafPolicy": "standard",
      "Egress": { "AllowedHosts": ["origin-b.example.com"], "AllowedPorts": [443], "AllowedSchemes": ["https"], "AllowPrivateExceptions": false }
    }
  }
}
```

The Desktop Site Manager is read-only for the active runtime configuration and indicates that a Gateway restart is required after configuration-file changes. This avoids changing YARP routes without a validated atomic reload.

## Remaining Production gates

- MFA/WebAuthn or OIDC for administrators (intentionally skipped per project instruction).
- Independent multi-instance concurrency validation for distributed authentication and route-aware rate limiting.

The PostgreSQL route limiter is fail-closed and bounded for long-running deployments: each transaction takes a database advisory lock for the shared capacity check, removes expired bucket rows, and rejects new bucket identities when `Waf:RateLimit:MaxEntries` would be exceeded. Existing identities can continue to be evaluated without allowing unbounded bucket growth. Validate the configured cap against expected Site × route × client cardinality before production rollout.
- SIEM export and alerting when audit-chain verification fails.
- WebSocket/Upgrade regression tests and a full external DNS-rebinding assessment between validation and connection establishment.
- Continuous SAST/secret scanning, independent load-test review and external penetration testing. Deterministic fuzz regression coverage and a local load-test harness are included; external evidence is still pending.
- A least-privilege PostgreSQL deployment account instead of a superuser.
- Set `Database:RequireLeastPrivilege=true` in Production; startup then fails closed when the database role is a superuser.

OWASP badges in the GUI describe coverage mapping only; they are not a security certification.
