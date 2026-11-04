# Module 214 — Shared Site ID Validation Plan

1. Add a source contract requiring the management editor to use the shared Site ID validator.
2. Run the contract and record the expected RED failure.
3. Remove the duplicate editor regex and delegate to `IsValidSiteId`.
4. Run the editor and Multi-Site regression suites.
5. Build/publish the self-contained artifact, verify provenance, and update handoff.
