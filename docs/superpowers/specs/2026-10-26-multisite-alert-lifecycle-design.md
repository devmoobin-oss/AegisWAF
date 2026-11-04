# Multi-site Alert Lifecycle and Cross-site Comparison Design

## Intent

Make the health alert useful to operators after Module 15 exposed it in the Desktop console. Operators need to acknowledge an actionable alert for a bounded cooldown, and they need a bounded comparison view for all sites without increasing raw health-history retention.

## Goals

- Persist one idempotent acknowledgement per site and alert state.
- Bound cooldown to 60 seconds through 24 hours and expose the remaining suppression state.
- Record acknowledgement actions in the existing tamper-evident audit chain.
- Allow only operator-level or higher roles to acknowledge; viewers can read.
- Return a bounded cross-site summary with per-site trend counts and stale/no-data state.
- Keep raw health observations capped at 64 per site and comparison results capped at 100 sites.

## Non-goals

- Email, webhook, push, or external notification delivery.
- Mutating health state or suppressing probes/routing; acknowledgement only suppresses repeated operator alerting.
- Increasing raw history retention or exposing upstream addresses/reasons.

## Approach

Add migration `016_site_health_alerts.sql` with a primary key `(site_id, alert_state)` and acknowledgement metadata. The store performs an upsert plus audit write in one transaction, making repeated acknowledgements idempotent and auditable. The trend endpoint includes acknowledgement metadata. A new summary endpoint uses a single bounded PostgreSQL window query to load up to 64 observations per site, then applies the existing pure trend calculator. The Desktop grid can acknowledge actionable states and show a compact cross-site summary.

## Safety and lifecycle rules

- Only `critical` and `degraded` states can be acknowledged; `healthy` and `no-data` return a conflict.
- Acknowledgement is valid only while the alert state matches the stored state; a changed state naturally stops suppression.
- Cooldown input is clamped/rejected outside 60..86400 seconds.
- Summary output contains site IDs, counts, state, acknowledgement metadata, and timestamps only.

## Acceptance gates

- TDD red/green for cooldown validation, idempotent persistence, state mismatch, and bounded summary calculation.
- PostgreSQL integration verifies one acknowledgement row after repeated requests and an audit record.
- Viewer/operator authorization contracts pass.
- Desktop contract exposes acknowledgement and summary states without breaking existing actions.
- Build, focused tests, and full regression pass.
