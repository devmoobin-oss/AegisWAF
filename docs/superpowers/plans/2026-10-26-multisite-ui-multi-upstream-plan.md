# Plan: Multi-Site UI multi-upstream management

## Task 1 — RED contract

- Extend `UiContractTests` with the multi-upstream field, syntax guidance, parser, `Upstreams` payload, and edit-round-trip requirements.
- Run the focused/static contract check before implementation and record the expected failure.

## Task 2 — Implement

- Add a multiline upstream editor with Thai syntax/help text.
- Parse and validate bounded IDs, URLs, duplicate IDs, and weights.
- Send all upstreams and derive safe egress lists from the complete set.
- Populate all upstreams when editing a Site.

## Task 3 — Verify and hand off

- Run the standalone verifier, Gateway build, and `git diff --check`.
- Record Module 52 before/after progress and the next opportunity in `handoff.md`.
