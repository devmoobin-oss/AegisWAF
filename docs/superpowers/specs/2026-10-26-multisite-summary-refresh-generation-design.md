# Multi-site Module 26: Refresh Result Ordering and View Identity

## Goal

Guarantee that only the newest refresh generation can render into the current Multi-site view, even if an older transport returns after cancellation.

## Scope

- Add a monotonic, process-local generation to the existing refresh lifecycle.
- Capture the generation at refresh start and check it after every awaited API boundary and before error rendering.
- Invalidate the generation on `Unloaded` together with cancellation.
- Keep the existing cancellation token and explicit retry semantics.

## Constraints

- No network changes, polling, or automatic retry.
- Generation contains no Site, user, database, or infrastructure data.
- Saturate generation at `long.MaxValue` rather than wrapping.
- Do not mutate recovery telemetry or controls for superseded generations.

## Acceptance gates

1. Test-first generation tests fail before the guard exists.
2. Focused generation/cancellation/diagnostics/freshness/state/API/UI/database tests pass.
3. Solution and runner builds pass with zero warnings/errors.
4. `git diff --check` passes and `handoff.md` records evidence.
