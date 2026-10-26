# Multi-site Module 21: Stale Summary Recovery and Operator Event History

## Goal

Make repeated health-summary failures understandable and recoverable in the Desktop Multi-site view without polling, duplicate requests, or leaking infrastructure details.

## Scope

- Keep the last successful summary in a bounded client-side recovery state.
- Mark the retained summary stale after a deterministic threshold of two consecutive failures.
- Keep a bounded local event history containing only `failure`/`recovered`, timestamp, and numeric consecutive-failure count.
- Show stale/recovery context in the existing summary slot while retaining Site rows and actions.
- Keep the existing explicit `ลองใหม่` action as the only retry mechanism.

## Security and reliability constraints

- Never store or render exception messages, SQL, hostnames, database names, connection strings, or stack traces in the recovery history.
- Bound history to eight events and saturate the consecutive-failure counter.
- A successful response replaces the retained summary and resets the consecutive-failure counter.
- No timer, background polling, or automatic retry is introduced.

## Acceptance gates

1. Test-first red evidence for the recovery state contract.
2. Focused recovery-state and Desktop UI contract tests pass.
3. PostgreSQL-backed API and full regression remain green.
4. Solution and in-process runner builds pass with zero warnings/errors.
5. `git diff --check` passes and `handoff.md` records before/after percentages and evidence.
