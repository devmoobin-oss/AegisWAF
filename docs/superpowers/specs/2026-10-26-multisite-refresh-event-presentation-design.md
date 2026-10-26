# Multi-site Module 28: Refresh Event Presentation and Operator Filtering

## Goal

Present the bounded cancellation history in the Multi-site Recovery Expander with a safe operator filter.

## Scope

- Expose an in-memory allowlist filter for `all`, `superseded`, `unloaded`, and `retry`.
- Render only the bounded cancellation events and existing recovery events as safe labels.
- Add a keyboard-focusable ComboBox with an accessible automation name.
- Keep the copy-safe diagnostics action and all filtering local-only.

## Constraints

- Unknown enum values are never rendered as raw input; they map to `none`/no filter.
- The UI renders at most the latest three cancellation events and latest three health-recovery events.
- No Site IDs, hosts, exceptions, SQL, database details, or transport payloads are rendered.
- No polling, retry, or API request is triggered by changing the filter.

## Acceptance gates

1. Test-first filter tests fail before the query contract exists.
2. Focused filter/state/diagnostics/freshness/cancellation/API/UI/database tests pass.
3. Solution and runner builds pass with zero warnings/errors.
4. `git diff --check` passes and `handoff.md` records before/after evidence.
