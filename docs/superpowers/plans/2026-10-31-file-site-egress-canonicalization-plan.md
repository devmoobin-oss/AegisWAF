# Module 212 — File Site Egress Canonicalization Plan

1. Add a loader regression using a valid case/trailing-dot Egress host and uppercase scheme.
2. Run the focused test and record the expected RED failure.
3. Normalize explicit Egress hosts and schemes in `SiteConfigurationLoader.BindSite`.
4. Run the focused test, configuration/source regression, and production Egress tests.
5. Build/publish the self-contained artifact, verify manifest/hash/PE header, and update handoff.
