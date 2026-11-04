# Multi-site Module 27: Refresh Observability and Cancellation Reason Taxonomy

## Goal

Make normal refresh cancellation observable without counting it as a health-summary failure or exposing infrastructure details.

## Scope

- Classify real cancellation events as `superseded`, `unloaded`, or `retry`.
- Keep a bounded local cancellation history of at most eight events plus a saturating numeric count.
- Include only the last safe reason in the bounded diagnostics string.
- Mark a refresh complete so starting a later refresh does not create a false cancellation event.

## Constraints

- Cancellation reasons are a closed enum; unknown values render as `unknown`.
- No cancellation event increments summary failure telemetry.
- No exceptions, SQL, database names, hosts, ports, connection strings, Site IDs, or stack traces are stored/rendered.
- No polling, automatic retry, or network endpoint is added.

## Acceptance gates

1. Test-first taxonomy/lifecycle tests fail before the new reason contract exists.
2. Focused taxonomy/cancellation/diagnostics/freshness/state/API/UI/database tests pass.
3. Solution and runner builds pass with zero warnings/errors.
4. `git diff --check` passes and `handoff.md` records before/after evidence.
