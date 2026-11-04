# Hosted security verification and release provenance plan

## Task 1 — manifest self-test (TDD)

- Add a PowerShell self-test before the writer exists and capture the RED result.
- Implement the manifest writer with safe metadata and file hashes.
- Run the self-test to verify the GREEN result and tamper detection.

## Task 2 — hosted workflow integration

- Add an opt-in `publish_desktop` workflow input.
- Publish the self-contained Desktop bundle on Windows and generate the manifest.
- Upload the bundle and manifest as a named artifact without changing the default PR gate.

## Task 3 — verification and handoff

- Run the manifest self-test, solution/runner builds, focused regression, and diff checks.
- Run the writer against the current published bundle when available.
- Record local evidence, hosted-run pending state, before/after percentages, and the next opportunity in `handoff.md`.
