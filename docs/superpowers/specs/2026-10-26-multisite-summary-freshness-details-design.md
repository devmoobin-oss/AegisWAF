# Multi-site Module 22: Summary Freshness Policy and Accessible Recovery Details

## Goal

Make the age of retained health-summary data explicit and give operators a compact, keyboard-accessible view of the bounded recovery history.

## Scope

- Treat a server-generated summary as stale when it is at least five minutes old.
- Use an injected/reference `DateTimeOffset` in the freshness calculation so tests are deterministic and the client clock cannot silently make future timestamps stale.
- Keep the existing two-failure stale threshold; freshness age is an independent signal.
- Place the bounded recovery history inside a WPF `Expander` so keyboard users can open it without a new request.

## Constraints

- Do not add polling, timers, automatic retries, or network calls.
- Do not expose infrastructure or exception details.
- A missing or future server timestamp is not treated as stale by age; the existing failure-count signal remains authoritative.

## Acceptance gates

1. Test-first freshness tests fail before the new contract exists.
2. Freshness and UI contract tests pass.
3. PostgreSQL/API compatibility and the existing focused regression remain green.
4. Solution and runner builds have zero warnings/errors; `git diff --check` passes.
5. Update `handoff.md` and record a verification artifact.
