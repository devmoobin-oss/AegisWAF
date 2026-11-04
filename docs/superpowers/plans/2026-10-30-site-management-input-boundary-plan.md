# Module 196 implementation plan

- [x] Identify silent filtering of invalid Hostname and Egress values in `TryNormalize`.
- [x] Add RED tests for malformed Hostname, wildcard Egress host, duplicate/out-of-range port, and non-HTTP scheme.
- [x] Reject malformed explicit values at the API input boundary while preserving empty-list derivation.
- [x] Run focused tests and the Multi-Site source regression.
- [x] Build Release and publish the self-contained single-file EXE.
- [ ] Validate the same errors through an authenticated live Browser/EXE request with PostgreSQL running.
