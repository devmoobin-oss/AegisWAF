# Module 213 — Production Egress Coverage Canonicalization

## Opportunity and root cause

After Module 212, file-backed runtime snapshots canonicalized explicit Egress hosts, but production validation still compared the upstream URI host against the raw allowlist. The same mismatch existed in both configuration-section validation and typed snapshot validation, so an equivalent trailing-dot/case variant could be rejected before runtime activation.

## Contract

- Production Egress coverage compares normalized upstream hosts with normalized Egress allowlist hosts.
- The rule applies to both `Validate(IConfiguration)` and `ValidateSnapshot(...)`.
- Port and scheme coverage rules remain unchanged.
- The accepted configuration, runtime snapshot, and Egress evaluator now share the same host identity.

## Non-goals

- No relaxation of production private-address, scheme, port, or allowlist requirements.
- No database or API schema change.

## Verification

- RED: production configuration validation reported that the canonical-equivalent host did not cover the upstream.
- GREEN: configuration and typed-snapshot coverage regression passes after both validation paths use normalized hosts.
