# Multi-Site health persistence acknowledgement

## Opportunity

After a health-history write failure, the process-local cache retained the probe, but it had no per-observation durable state. A later successful database read could therefore merge pending process-memory observations while still reporting `historyPersisted=true`.

## Module 169 scope

- Track pending observations per Site in the bounded process-local cache.
- Clear pending keys only after `RecordHealthHistoryAsync` completes successfully.
- Report `postgresql+process-memory` when PostgreSQL is readable but pending observations remain; report `process-memory` when the database history read is unavailable.
- Preserve bounded eviction, duplicate-safe merge, topology filtering, and existing persistence-integrity failure behavior.
- Keep the public API backward compatible by adding only response fields/retention values.

## TDD and verification

1. Add a red test proving pending observations are not durable until explicitly marked persisted.
2. Wire the manual probe success path to `MarkPersisted` and expose pending-aware retention.
3. Run health/history/trend/summary/topology contracts and the existing persistence-outcome contracts.
4. Build Release with zero warnings/errors and run diff checks.
5. Publish the completed-modules bundle from a clean source commit and verify its manifest/SHA-256.

## Completion

Module 169 is complete at 100% when the pending-state tests, focused regression, Release build, diff checks, and release-manifest verification pass. Live PostgreSQL failure/recovery execution and authenticated published-EXE smoke remain environment-dependent gates.
