# Multi-site Health Trend and Operator Alerting Design

## Intent

Give operators a compact, bounded signal from the PostgreSQL-backed Multi-site health history. The signal must be deterministic, safe to expose through the API, and cheap to calculate on demand.

## Goals

- Aggregate a caller-selected history window, capped by the existing 64-observation retention and a 100-row API limit.
- Report total, healthy, unhealthy, unknown, healthy percentage, newest check time, and consecutive unhealthy observations.
- Report an explicit operator state: `no-data`, `healthy`, `degraded`, or `critical`.
- Make `critical` deterministic at three or more newest consecutive unhealthy observations.
- Keep the calculation stateless; no duplicate alert rows or unbounded notification state are introduced.

## Non-goals

- Sending email, desktop notifications, or external webhooks.
- Changing probe behavior, persistence retention, or upstream routing.
- Exposing upstream addresses, credentials, or raw exception details.

## Approach

The existing PostgreSQL health-history query is reused and the gateway computes a bounded projection in memory. A pure `SiteHealthTrendCalculator` owns all classification rules so unit tests can cover ordering, empty history, mixed health states, and threshold behavior. The API exposes the projection at `/api/sites/{id}/health/trend?window=N`; the typed client mirrors it for the Multi-site UI.

## Constraints and safety

- Window is clamped to 1..64 before the database query.
- Health values are interpreted only as `healthy`, `unhealthy`, and `unknown`; unexpected values become `unknown`.
- The projection contains identifiers, counts, percentages, and timestamps only.
- PostgreSQL remains the source of truth across process restart.

## Acceptance gates

- TDD red/green evidence for pure trend classification and API contract.
- Solution and runner builds with zero warnings/errors.
- PostgreSQL integration proves a persisted three-failure sequence returns `critical`.
- Full regression remains green.
