# Multi-site Module 24: Recovery Diagnostics Lifecycle and Retention Reset

## Goal

Make recovery diagnostics valid only for the currently rendered refresh and keep long-lived Desktop sessions bounded and predictable.

## Scope

- Invalidate the prior diagnostics/token at the start of every summary refresh.
- Enable diagnostics copying only after the current refresh has rendered a summary or failure state.
- Preserve the bounded eight-event recovery history, while resetting the active failure state on a fresh success.
- Keep diagnostics text bounded to 256 characters.
- Hide and disable the recovery details/copy action during refresh so stale support data cannot be copied.

## Constraints

- Do not clear the bounded audit-like recovery event history merely because a refresh succeeds; keep the recovery event for operator context.
- Do not add polling, timers, retries, network calls, or persistence.
- Do not expose exceptions, SQL, database names, hosts, ports, connection strings, Site IDs, or stack traces.

## Acceptance gates

1. Test-first lifecycle tests fail before the new invalidation contract exists.
2. Focused lifecycle/state/UI/API/database tests pass.
3. Solution and runner builds pass with zero warnings/errors.
4. `git diff --check` passes and `handoff.md` records before/after evidence.
