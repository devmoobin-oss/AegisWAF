# Multi-site Module 23: Operator Summary Diagnostics and Refresh Correlation

## Goal

Give an operator a safe, copyable identifier for one local summary refresh and a compact diagnostics string that can be included in a support handoff.

## Scope

- Generate a short opaque hexadecimal correlation token at the start of each Desktop refresh.
- Keep the token process-local; it is never sent to the Gateway or persisted.
- Expose only token, numeric failure count, stale flags, and bounded age in the diagnostics text.
- Add a keyboard-accessible copy action inside the existing recovery details Expander.

## Security constraints

- Diagnostics must not contain exceptions, SQL, database names, hosts, ports, connection strings, Site IDs, or stack traces.
- Bound the rendered diagnostics length and use a fixed key/value format.
- Copying is local clipboard state only; it must not trigger a network request or retry.

## Acceptance gates

1. Test-first red evidence for correlation and diagnostics contracts.
2. Focused state/UI/API/database tests pass.
3. Solution and runner builds pass with zero warnings/errors.
4. `git diff --check` passes and `handoff.md` records evidence.
