# Plan: standard VSTest Multi-Site regression gate

## Task 1 — Reproduce and classify

- Run focused `dotnet test` without loopback permission and capture diagnostics.
- Run the same focused command with loopback permission to separate environment blocking from test failures.

## Task 2 — Add bounded wrapper

- Write the contract test first.
- Add a PowerShell wrapper over the existing bounded `Invoke-Gate` helper.
- Parse exact total/passed/failed/skipped counts and classify testhost connection limits separately.

## Task 3 — Verify and hand off

- Run the contract test.
- Run the wrapper with loopback permission and require 27/27.
- Run `git diff --check` and record evidence and limitations in `handoff.md`.
