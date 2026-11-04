# Plan: xUnit Multi-Site regression gate

## Task 1 — Diagnose

- Restore and build the test project with the current SDK.
- Use the compiler output to fix only the actual test/project issue.

## Task 2 — Run deterministic gate

- Build the in-process xUnit runner.
- Run the four focused Multi-Site test classes and require exact 27/27 pass evidence.

## Task 3 — Handoff

- Add a repeatable PowerShell evidence wrapper.
- Record before/after progress, evidence, and the remaining VSTest-host limitation in `handoff.md`.
