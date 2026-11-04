# Plan: Multi-Site UI per-Site policy fields

## Task 1 — RED contract

- Extend `UiContractTests` for Health/RateLimit fields, Thai guidance, validation bounds, request payload, and edit population.
- Confirm the pre-implementation contract fails.

## Task 2 — Implement

- Add Health and Rate Limit controls to the browser Multi-Site form.
- Validate bounds client-side and send `Health`/`RateLimit` objects.
- Restore saved values when editing.

## Task 3 — Verify and hand off

- Run standalone contract verification, JavaScript syntax check, Gateway build, and diff check.
- Record Module 53 progress and the next opportunity in `handoff.md`.
