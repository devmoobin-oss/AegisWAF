# Multi-site Module 25: Summary Refresh Cancellation and Shutdown Safety

## Goal

Prevent an in-flight Multi-site summary refresh from updating controls after the Desktop view is unloaded or superseded by a newer refresh.

## Scope

- Create a cancellable local refresh lifecycle that cancels the previous refresh when a new one starts.
- Pass the cancellation token through Site and health-summary API calls.
- Cancel the active refresh on WPF `Unloaded` and disable retry/copy details controls.
- Ignore cancellation as a normal lifecycle event; do not display it as an API error.

## Constraints

- No automatic retry, polling, or new network endpoint.
- Do not swallow non-cancellation API errors.
- Do not update disposed/unloaded controls after cancellation.
- Preserve the existing bounded recovery state and explicit retry behavior.

## Acceptance gates

1. Test-first lifecycle tests fail before the cancellation contract exists.
2. Focused lifecycle/state/UI/API/database tests pass.
3. Solution and runner builds pass with zero warnings/errors.
4. `git diff --check` passes and `handoff.md` records evidence.
