# Module 212 — File Site Egress Canonicalization

## Opportunity and root cause

File-backed Site configuration validation compares Egress hosts and schemes semantically, but `SiteConfigurationLoader.BindSite` copied explicit values into the runtime snapshot without applying the same canonicalization. For example, a valid `ORIGIN.EXAMPLE.COM.` allowlist entry could remain uppercase and dotted while the upstream URI exposed `origin.example.com`, causing the runtime Egress policy to deny a valid destination.

## Contract

- Explicit Egress hosts are normalized with the shared hostname normalizer before entering the runtime snapshot.
- Explicit Egress schemes are trimmed and lowercased before entering the runtime snapshot.
- Default Egress values derived from upstream URIs remain unchanged in meaning.
- Validation remains the rejection boundary; this module only makes accepted configuration deterministic at runtime.

## Non-goals

- No database schema or API payload change.
- No change to private-address or production Egress policy decisions.

## Verification

- RED: the loader regression failed because the runtime retained ` ORIGIN.EXAMPLE.COM. `.
- GREEN: the loader regression and the existing configuration regression suite pass after canonicalization.
