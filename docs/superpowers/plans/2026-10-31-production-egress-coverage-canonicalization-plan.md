# Module 213 — Production Egress Coverage Canonicalization Plan

1. Add one regression covering configuration and typed-snapshot production validation with a case/trailing-dot Egress host.
2. Run the focused test and record the expected RED failure.
3. Compare normalized upstream hosts against normalized Egress hosts in both validator paths.
4. Run configuration, Egress, Multi-Site source, and CRUD regressions.
5. Build/publish the self-contained artifact, verify its manifest/hash, and update handoff.
