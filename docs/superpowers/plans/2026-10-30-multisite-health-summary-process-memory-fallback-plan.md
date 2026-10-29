# Multi-Site health summary process-memory fallback

## Opportunity

The Multi-Site Summary endpoint still treated a transient PostgreSQL failure while reading `waf_health_history` as a complete summary outage, even though manual Health already reported a process-memory retention boundary and History/Trend could use that fallback.

## Module 168 scope

- Inject the bounded `SiteHealthHistory` cache into the Summary endpoint.
- Keep Site paging, acknowledgement reads, and persistence-integrity failures on their existing guarded paths.
- If only the history batch read has a transient database failure, calculate Summary from process-memory observations and expose `historyPersisted=false` / `historyRetention=process-memory`.
- Merge durable and process-local observations to avoid duplicate probes after PostgreSQL recovers.
- Preserve topology filtering, alert thresholds, stale/recovery semantics, pagination, and query-budget behavior.
- Add client DTO fields so desktop callers can observe the persistence boundary.

## TDD and verification

1. Add a contract test and confirm it fails before the Summary fallback exists.
2. Implement the isolated history fallback and merge path.
3. Run Summary, alert-count, topology, pagination, and truncation contracts.
4. Run the combined Multi-Site focused regression and Release build with zero warnings/errors.
5. Publish the completed-modules bundle only after a clean source commit, then verify manifest and SHA-256.

## Completion

Module 168 is complete at 100% when focused tests, Release build, diff checks, and release-manifest verification pass. Live PostgreSQL outage execution, full VSTest testhost connectivity, and authenticated published-EXE smoke remain environment-dependent gates.
