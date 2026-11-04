# Plan: Multi-Site UI truthful activation and health copy

## Task 1 — RED contract

- Update `UiContractTests` to require immediate activation wording, Thai health-state copy, and no stale restart warning.
- Run the focused test/build command and record the expected failure before implementation.

## Task 2 — Implement the browser UI contract

- Add bounded state/reason helpers to the Multi-Site renderer.
- Replace restart-required copy with immediate activation copy while retaining explicit `pending-restart` rendering.
- Render health status and a safe explanation consistently in Thai.

## Task 3 — Verify and hand off

- Build the Gateway, run the standalone/static contract verifier if the test project remains SDK-blocked, and run `git diff --check`.
- Write before/after progress and evidence to `handoff.md`, then audit the next opportunities.
