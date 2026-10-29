# Multi-Site health history process-memory fallback

## Opportunity

The manual Health endpoint reports `historyRetention=process-memory` when a PostgreSQL write fails, but the History and Trend endpoints previously queried PostgreSQL only. A transient database failure therefore made the advertised fallback unavailable to operators.

## Module 167 scope

- Keep the bounded process-local cache as a read fallback for History and Trend.
- Merge durable and process-local observations with deterministic newest-first ordering and duplicate removal.
- Resolve the active runtime Site from `SiteRegistry` when a transient database read failure prevents the initial Site lookup.
- Preserve `InvalidDataException` as a hard persistence-integrity failure.
- Expose the persistence boundary on Trend and preserve the existing History contract.
- Do not change alert thresholds, topology filtering, or the raw audit meaning of persisted history.

## TDD and verification

1. Add merge tests for deduplication, ordering, and bounded windows; confirm the new tests fail before implementation.
2. Add endpoint contract tests proving both endpoints read/merge process memory and report fallback state.
3. Run focused health, topology, budget, and efficiency contracts.
4. Build the full solution in Release with zero warnings/errors.
5. Run the full VSTest suite; if the testhost infrastructure cannot connect, record that boundary rather than claiming a pass.
6. Publish only after the source commit is clean, then verify the release manifest and SHA-256.

## Completion

Module 167 is complete at 100% when the focused tests, Release build, source diff checks, and release-manifest verification pass. Live PostgreSQL outage execution and authenticated published-EXE smoke remain environment-dependent gates.
