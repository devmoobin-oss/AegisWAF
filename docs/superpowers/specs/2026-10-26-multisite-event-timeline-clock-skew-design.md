# Multi-site Module 29: Event Timeline Ordering and Clock-Skew Handling

## Goal

Keep refresh cancellation event presentation deterministic when timestamps are equal or move backward because of clock skew.

## Scope

- Add a monotonic sequence to each local cancellation event.
- Preserve queue/filter order by sequence, not by wall-clock timestamp.
- Keep timestamp as informational metadata only.
- Render a bounded sequence label in the Recovery Expander.

## Constraints

- Sequence is process-local, contains no infrastructure data, and saturates rather than wrapping.
- Event history remains bounded to eight entries and UI presentation to three entries.
- No timestamp normalization or client clock correction is used.
- No polling, API request, retry, or sensitive payload is introduced.

## Acceptance gates

1. Test-first clock-skew tests fail before the sequence contract exists.
2. Focused ordering/filter/state/diagnostics/freshness/cancellation/API/UI/database tests pass.
3. Solution and runner builds pass with zero warnings/errors.
4. `git diff --check` passes and `handoff.md` records before/after evidence.
