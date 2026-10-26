# AegisWAF Production Readiness

## Runtime baseline enabled

- Deployment is host-native: the Windows Desktop/standalone gateway does not require Docker or a container runtime. Container-based deployment is intentionally out of scope for this release.

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
- `/ready` also checks the PostgreSQL Site-runtime listener; reconnecting is reported as degraded and a stopped listener is unhealthy, without exposing connection details.
- `/ready` also verifies every enabled explicit Site has a YARP cluster with at least one destination that is not marked unhealthy by either active or passive health checks; unknown destinations remain eligible during initial probing.
- Compressed request bodies and protocol upgrades are denied by default because the WAF cannot inspect them safely; enable `Waf:AllowProtocolUpgrade` only for a dedicated, tested route.
- An opt-in fail-closed deployment gate is available with `Waf:Deployment:Enforce=true`; it rejects auto-migration, superuser database roles, unsafe methods, unsafe upgrades, missing rate-limit bounds, and wildcard hosts for remote administration.
- SSRF filtering blocks loopback, RFC1918/link-local, metadata hostnames, IPv4 CGNAT, IPv4 multicast/reserved ranges, IPv6 Unique Local and IPv6 multicast targets, including encoded and non-canonical IPv4 forms.
- Response inspection can fail closed for oversized textual responses with `Waf:ResponseOverflowAction=block`; the default Desktop compatibility profile remains `stream` and must be changed for a strict Production deployment.
- If upstream headers were already committed when a fail-closed response exceeds the inspection cap, the gateway aborts the stream instead of returning an apparent success with a truncated body.
- Production enforcement requires `Waf:InspectResponses=true`; disabling response inspection is permitted only in the non-production Desktop compatibility profile.
- Production enforcement requires `Waf:InspectBinaryRequests=true`; binary-labeled request bodies receive bounded byte-safe raw inspection instead of bypassing the WAF.
- Production enforcement requires `Waf:InspectBinaryResponses=true`; bounded binary responses are scanned through a byte-safe Latin-1 projection for high-signal leaks even when an upstream mislabels sensitive text as binary.
- Textual upstream responses with malformed UTF-8 are rejected before response-leak rules run, preventing replacement-character decoding from changing the inspected representation.
- Admin/control-plane responses send `no-store`, `X-Frame-Options: DENY`, COOP/CORP, `X-Permitted-Cross-Domain-Policies: none`, CSP and restrictive Permissions-Policy headers.
- Rate limiting supports both per-client and global rolling-window ceilings. Production uses the PostgreSQL shared store so multiple WAF instances enforce one common limit; run migration `008_rate_limit_buckets.sql` before enabling traffic.
- `/ready` verifies the shared rate-limit table exists when `Store=postgres`; traffic should not be admitted until this migration check is healthy.
- Security events are emitted as structured `aegiswaf.security_event` JSON logs with client, request, status and OWASP mapping fields, allowing a SIEM/agent to ingest events even when database persistence is temporarily unavailable.
- Security-event persistence uses a bounded queue and configurable batch transaction (`Waf:Events:QueueCapacity`, `BatchSize`, `FlushMilliseconds`); the `eventDelivery` field in `/api/overview` exposes queue depth, persisted count and persistence failures, while dropped events remain fail-visible through `WAF_SECURITY_EVENT_DROPPED`.
- Security-event retention is bounded and server-clock based: `Waf:Events:RetentionHours` controls expiry (1–8760 hours) and `CleanupIntervalSeconds` controls cleanup cadence (60–86400 seconds). `/api/overview.securityEventRetention` reports active/disabled/degraded state, last cleanup and deleted-row count. Retention is independent of SIEM delivery; external SIEM remains the long-term archive.
- PostgreSQL Site runtime convergence exposes a redacted `siteRuntimeListener` status in `/api/overview` (`state`, connection attempts, connected/disconnected/reload timestamps and exception type only). It is diagnostic state, not proof that an upstream is healthy; use `/ready` and per-Site health for those gates.
- Standalone Windows hosting uses an explicit console logging provider instead of an implicit EventLog sink, so EventLog source permissions cannot crash `EventWriter` and stop security-event processing.
- Production deployments must provide `Security:DataProtection:KeyRingPath` as an absolute persistent directory; the Gateway persists keys there and protects them with Windows DPAPI. On non-Windows hosts, also provide an absolute `Security:DataProtection:CertificatePath` and secret `CertificatePassword`. Do not reuse a user-profile key ring across service identities.
- Production `/ready` includes a Data Protection protect/unprotect round-trip; an unreadable key ring or failed DPAPI/certificate operation keeps the instance out of readiness.
- If `/api/audit/integrity` detects a broken audit chain, the Gateway emits a structured `aegiswaf.audit_integrity_failure` event at critical level with the checked count and first invalid row ID, so SIEM alerting does not depend on an operator noticing the API response.
- Audit-chain hashing canonicalizes timestamps to PostgreSQL microsecond precision before hashing; legacy rows are accepted only before the first chained row, while a null-hash row inserted after the chain starts is reported as tampering.
- Structured events include the originating `requestId`, enabling correlation across WAF, SIEM and upstream traces without changing the existing database schema.
- A failed `/api/audit/integrity` check emits a deduplicated `Audit Integrity Failure` security event with the first invalid row id and request id; it does not write into the compromised audit chain.
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
- Production enforcement also requires `Waf:Limits:MaxRequestBodyBytes` to be positive, bounded to 16 MiB, and no larger than `Waf:MaxBodyInspectBytes`; Kestrel cannot admit a request body larger than the parser inspection budget.
- Production enforcement bounds `Waf:Limits:RequestHeadersTimeoutSeconds` to 1–120 seconds and `Waf:Limits:KeepAliveSeconds` to 5–600 seconds, preventing an unsafe timeout configuration from weakening slow-client protection.
- Production enforcement bounds `Waf:Limits:MaxRequestLineBytes` to 1–64 KiB and `Waf:Limits:MaxRequestHeadersBytes` to 4–256 KiB, preventing oversized framing/header limits from becoming a configuration-driven memory/DoS risk.
- Production enforcement bounds and applies `Waf:Limits:MaxConcurrentConnections` to 1–1,000,000 (runtime default 10,000), preventing an unbounded Kestrel connection pool from becoming a configuration-driven resource-exhaustion risk.
- Production enforcement bounds and applies `Waf:Limits:MinRequestBodyBytesPerSecond` (1–1,000,000) with a 1–120 second grace period, preventing slow request-body uploads from pinning Kestrel connections indefinitely.
- Production enforcement bounds and applies `Waf:Limits:MinResponseBytesPerSecond` (1–1,000,000) with a 1–120 second grace period, preventing slow clients from pinning response connections indefinitely.
- Production enforcement bounds each configured YARP `HttpRequest:ActivityTimeout` to 1 second–5 minutes when specified, preventing an accidentally unbounded upstream request from pinning gateway resources indefinitely.
- Production enforcement rejects global rate-limit values outside the runtime bounds (`Requests` 1–1,000,000; `WindowSeconds` 1–3,600; `MaxEntries` 100–1,000,000; `GlobalRequests` 1–10,000,000) instead of silently clamping an operator's configuration.
- Production enforcement also rejects distributed authentication throttle values outside their runtime bounds (`MaxFailures` 1–1,000; `WindowMinutes` 1–1,440; `MaxEntries` 100–1,000,000), keeping shared login-abuse protection explicit across instances.
- Production enforcement bounds administrative session idle/absolute lifetimes and account lockout settings (`SessionIdleMinutes` 1–1,440; `SessionAbsoluteHours` 1–168; `MaxFailedAttempts` 1–100; `LockoutMinutes` 1–1,440), preventing accidental indefinite sessions or disabled lockout.
- When anomaly scoring is enabled, Production enforcement requires `Waf:AnomalyScoring:Threshold` to remain within the runtime range 3–100 instead of silently accepting a clamped value.
- Structured request inspection now extracts bounded JSON, form-urlencoded, XML, and multipart fields/metadata before rule evaluation; ordinary HTML DOCTYPE is allowed while entity/external-DTD payloads are rejected.
- XML DTD/entity detection uses a bounded ordinal scan rather than an unbounded backtracking expression, while rejecting external `SYSTEM`/`PUBLIC` declarations before XML parsing.
- JSON inspection rejects duplicate object keys before rule evaluation to prevent parser differentials where the WAF and upstream select different values for the same field.
- Form-urlencoded inspection is bounded while parsing and rejects malformed percent-encoding with a stable parser reason, avoiding unbounded pair materialization before the member limit is applied.
- Form-urlencoded percent escapes are decoded through a strict UTF-8 byte path; malformed encoded sequences such as `%C3%28` fail closed instead of being left partially decoded or normalized ambiguously.
- Multipart inspection scans boundary markers incrementally and enforces the part limit before continuing, avoiding an all-parts allocation for oversized multipart requests.
- Textual request bodies with malformed UTF-8 are rejected before parser/rule evaluation; the security/load-test harness supports explicit `Host` selection for multi-Site runs and uses the worker-scoped target URL.
- The configured inspection timeout now covers request-body read, decoding, raw rule evaluation, structured parsing, and parsed-value evaluation; expiry fails closed with `Inspection Timeout`.
- Large inspected values are evaluated through bounded overlapping canonical windows, so payloads after the former 65,536-character normalization cap are not silently skipped while per-window regex work remains bounded.
- The inspection budget is propagated into windowed rule evaluation for raw and structured body values; expiration returns `Inspection Timeout` before additional windows are evaluated.
- gzip, deflate, and Brotli request bodies are decompressed only within compressed-size, decompressed-size, expansion-ratio, and parser budgets; unknown encodings fail closed.
- Production upstreams require explicit HTTPS host/port/scheme allowlists and are checked against DNS-resolved private, loopback, metadata, multicast, reserved, and link-local targets before proxying.
- Production enforcement applies bounded `Waf:Egress:MaxConnectionsPerServer` and per-cluster `HttpClient:MaxConnectionsPerServer` caps (1–10,000; runtime default 256) to outbound YARP handlers, limiting connection amplification against an upstream.
- Production rejects configured YARP `HttpClient:WebProxy` sections, requires `HttpClient:UseProxy=false`, and disables automatic redirects; outbound traffic must use the reviewed connection-boundary egress path rather than an environment/system proxy or redirect target that could bypass the Site allowlist.
- Generated Multi-Site YARP clusters enable active `/health` probes and the explicit `TransportFailureRate` passive policy; destination selection uses `HealthyAndUnknown` so unhealthy upstreams are removed while unknown destinations remain eligible until the first probe completes.
- Each Site may define a bounded `Health` policy (`Path`, `IntervalSeconds`, `TimeoutSeconds`, `PassiveReactivationSeconds`); invalid paths or timing relationships fail configuration validation instead of producing unsafe probes.
- `/api/sites` projects runtime YARP destination health, so the Desktop view no longer treats an unprobed destination as healthy by default.
- Generated Multi-Site clusters explicitly enable YARP load balancing with `PowerOfTwoChoices`; configured upstream weights remain part of the Site model, while YARP health state controls availability.
- Real-socket integration coverage verifies two configured Sites route through the embedded gateway and an unknown Host is rejected with `421 Misdirected Request`; this complements, but does not replace, external multi-instance testing.
- Real-socket parser coverage verifies duplicate JSON object keys are rejected with `403` before the request reaches the upstream.
- Concurrent real-socket coverage sends 30 requests across two configured Sites plus an unknown Host; valid Site traffic remains `200` while unknown Host traffic remains `421`, demonstrating routing isolation under concurrency.
- Real-socket failover coverage drives transport failures past YARP's passive detection threshold and verifies the healthy destination remains selected afterward; this is local deterministic evidence, not external multi-instance testing.
- Under `Waf:Deployment:Enforce=true`, YARP's outbound `SocketsHttpHandler` connection callback rechecks the exact destination host/port and current DNS answer immediately before opening a socket, then connects to the validated address rather than resolving again after validation. This is defense-in-depth and does not replace infrastructure egress ACLs.
- Egress DNS validation is bounded to 32 answers per hostname and fails closed with `dns_answer_limit` when a resolver returns an unexpectedly large set.
- The egress connection guard preserves the complete validated DNS answer set and tries each validated address before failing, without resolving the hostname again after policy validation.
- Production startup rejects `DangerousAcceptAnyServerCertificate` and the egress classifier rejects special-use IPv4 ranges including unspecified, documentation, benchmark, and deprecated relay ranges, plus the IPv6 documentation range (`2001:db8::/32`).
- Production authentication throttling uses shared PostgreSQL state for normalized source and username keys; the WAF limiter also includes bounded route keys and the readiness check verifies the distributed authentication migration.
- Distributed authentication throttle capacity checks take a transaction-scoped PostgreSQL advisory lock and count missing identity keys before insertion, preventing concurrent instances from exceeding `Auth:Throttle:MaxEntries`.
- Real-socket integration starts two Gateway instances against one PostgreSQL database and verifies authentication throttling is shared across instances; a second test verifies Site request rate limiting returns `429` after the shared bucket is consumed across both gateways.
- The PostgreSQL rate limiter uses separate lock/cleanup and count statements; this avoids fragile SQL string slicing and is covered by the two-Gateway regression test.
- The current verification baseline is 328 passing tests with zero failures/skips; the assessment runner records the Git commit and source configuration SHA-256 fingerprint for each evidence run.
- A safe local security/load-test harness is available at `tools/security-loadtest/loadtest.ps1`; it emits JSON/CSV latency, throughput, status, block, and error evidence without destructive payloads.
- Optional listener isolation is available for production: bind `Admin:ListenerPort` and `Gateway:ListenerPort` to different ports. Admin paths (`/`, `/assets`, `/api`, `/health`, `/ready`) are then rejected on the gateway listener and protected-app paths are rejected on the admin listener. The default zero-port Desktop profile remains single-listener compatible.
- When a trusted TLS-terminating proxy is configured, forwarded scheme and host are honored only if the direct peer matches `Security:TrustedProxies`; remote-admin HTTPS and session-cookie `Secure` decisions use that same effective scheme.
- Local runtime evidence after the logging hardening: 80 requests at concurrency 2, 78 WAF blocks (403), 0 client errors, p95 latency 1.662 ms; 2 clean requests returned 502 because the intentionally absent local upstream was unavailable.
- Healthy-upstream runtime evidence: 82 requests at concurrency 2, 78 attack requests blocked (403), 4 clean requests proxied successfully (200), 0 errors, throughput 15.6 req/s and p95 latency 153.162 ms. This validates both the deny path and the allow/proxy path over real sockets.
- Multi-Site routing is configured under `Sites`; each Site has explicit hostnames, path prefixes, upstreams, rate limits and egress policy. Unknown hosts fail closed when Production enforcement is enabled.
- `WafPolicy` resolves to an immutable per-request snapshot when a matching `Waf:Policies:<name>` section exists. A Site policy may disable built-in, scanner, anomaly or custom rule IDs for that Site only; it cannot enable a rule disabled globally. Unknown names use the global fallback in non-production compatibility mode; Production startup rejects undefined names.
- Site-scoped rate-limit keys include SiteId and route, and structured security events include SiteId, hostname and upstream context. Run migration `010_site_event_context.sql` before enabling event persistence for Multi-Site.
- Production rejects per-Site rate limits outside bounded ranges instead of silently clamping them: requests `1..1,000,000`, window `1..3,600` seconds, and global requests `1..10,000,000`.
- Production Site egress allowlists reject wildcard or malformed host entries and ports outside `1..65,535`; upstream coverage must still match an explicit HTTPS host/port/scheme tuple.
- Production rejects upstream weights outside `1..10,000` instead of silently clamping them, keeping configured load-balancing/failover proportions explicit.
- Production rejects private, loopback, metadata, multicast, and special-use IP-literal upstreams during startup validation; DNS hostnames remain subject to the bounded preflight and connection-boundary checks.
- When Production enforcement is enabled, startup performs a DNS-aware egress preflight for every enabled Site upstream and fails before accepting traffic if the current answer is disallowed; the connection-boundary check remains mandatory for post-startup DNS changes.

## Required deployment configuration

```json
{
  "Admin": { "AllowRemote": false, "RequireHttps": true },
  "Security": {
    "TrustedProxies": ["10.0.0.0/8"],
    "DataProtection": { "KeyRingPath": "C:\\ProgramData\\AegisWAF\\keys" }
  },
  "Waf": {
    "Deployment": { "Enforce": true },
    "InspectResponses": true,
    "InspectBinaryRequests": true,
    "InspectBinaryResponses": true,
    "ResponseOverflowAction": "block",
    "Egress": { "MaxConnectionsPerServer": 256 },
    "RateLimit": { "Requests": 300, "GlobalRequests": 10000, "Store": "postgres", "WindowSeconds": 60, "MaxEntries": 10000 },
    "Limits": { "MaxRequestBodyBytes": 1048576 },
    "Policies": {
      "strict": { "DisabledRules": [] },
      "standard": { "DisabledRules": ["anomaly-01"] }
    }
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
      "Health": { "Path": "/health", "IntervalSeconds": 10, "TimeoutSeconds": 3, "PassiveReactivationSeconds": 30 },
      "Egress": { "AllowedHosts": ["origin-a.example.com"], "AllowedPorts": [443], "AllowedSchemes": ["https"], "AllowPrivateExceptions": false }
    },
    "site-b": {
      "Enabled": true,
      "Hostnames": ["app-b.example.com"],
      "PathPrefixes": ["/"],
      "Upstreams": [{ "Id": "primary", "Address": "https://origin-b.example.com/", "Weight": 100 }],
      "WafPolicy": "standard",
      "Health": { "Path": "/health", "IntervalSeconds": 10, "TimeoutSeconds": 3, "PassiveReactivationSeconds": 30 },
      "Egress": { "AllowedHosts": ["origin-b.example.com"], "AllowedPorts": [443], "AllowedSchemes": ["https"], "AllowPrivateExceptions": false }
    }
  }
}
```

`DisabledRules` is deliberately deny-only at Site scope. Keep the global rule state at its strongest safe baseline and use Site policies only for documented exceptions; a policy is not a replacement for per-Site rule storage or live reload.

The Desktop Site Manager can add a validated Site to `appsettings.local.json` using an atomic write. The active runtime configuration remains immutable until Gateway restart; the UI makes this state explicit and never changes YARP routes without a validated restart.

## Remaining Production gates

- MFA/WebAuthn or OIDC for administrators (intentionally skipped per project instruction).
- Independent multi-instance concurrency validation for distributed authentication and route-aware rate limiting.
- Independent SIEM delivery/retention and operational response validation for audit-chain alerts.
- WebSocket/Upgrade regression tests and a full external DNS-rebinding assessment between validation and connection establishment.
- Continuous SAST/secret scanning, independent load-test review and external penetration testing. Deterministic fuzz regression coverage and a local load-test harness are included; external evidence is still pending.

- The PostgreSQL route limiter is fail-closed and bounded for long-running deployments: each transaction takes a database advisory lock for the shared capacity check, removes expired bucket rows, and rejects new bucket identities when `Waf:RateLimit:MaxEntries` would be exceeded. Existing identities can continue to be evaluated without allowing unbounded bucket growth. Validate the configured cap against expected Site × route × client cardinality before production rollout.
- SIEM routing and alert policy configuration for the `aegiswaf.audit_integrity_failure` event.
- Full external DNS-rebinding assessment between validation and connection establishment; local real-socket coverage now verifies a valid WebSocket upgrade is rejected before proxying unless explicitly enabled for a dedicated route.
- Continuous SAST/secret scanning is configured in `.github/workflows/security-verification.yml` but still needs a successful hosted run. The same workflow provides a manual-only authorized load-test job that requires an operator-supplied synthetic/staging target. Independent load-test review and external penetration testing remain pending; deterministic fuzz regression coverage and a local load-test harness are included.
- A least-privilege PostgreSQL deployment account instead of a superuser.
- Set `Database:RequireLeastPrivilege=true` in Production; startup then fails closed when the database role is a superuser.

OWASP badges in the GUI describe coverage mapping only; they are not a security certification.
