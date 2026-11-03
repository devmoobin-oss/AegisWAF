# Module 210 — Database Site ID Boundary

## Opportunity

The primary `waf_sites` row and hostname rows already use canonical Site IDs, but several direct `SiteDatabaseStore` methods accepted caller-provided IDs without normalization. A caller using whitespace or different casing could therefore miss an existing site, create split health-history/acknowledgement records, or emit audit/notification targets that did not match the persisted identity.

## Contract

- Every Site ID entering a database-store read, health-history operation, acknowledgement operation, or delete operation is normalized with `SiteConfigurationValidator.NormalizeId` before SQL parameters are created.
- Batch Site ID filters are normalized, case-insensitively deduplicated, and bounded before being sent to PostgreSQL.
- Audit targets, NOTIFY payloads, returned acknowledgement objects, and database keys use the same canonical Site ID.
- Existing Site ID validation remains owned by configuration/API validation; this module establishes a consistent persistence identity boundary for direct store callers.

## Non-goals

- No schema migration is required.
- No change to public Site ID display semantics in API responses.
- No change to health alert severity rules.

## Verification

- TDD RED: boundary contract and direct-store test failed before the change.
- TDD GREEN: boundary/cardinality tests and direct-store canonical-input test passed.
- Multi-Site source regression and release artifact verification are recorded in the module evidence JSON and handoff pointer.
