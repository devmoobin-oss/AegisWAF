# Module 172 — Desktop OWASP resource scope

## Goal

Prevent the Desktop OWASP Top 10 page from failing at runtime with `IconFont resource not found` when the view is created outside the resource lookup scope expected by the view.

## Before / after

- Before: 0% — `OwaspCoverageView.Render()` calls `FindResource("IconFont")` on the `UserControl`, while the resource is defined in the application-level merged `Theme.xaml` dictionary. The reported runtime failure is reproducible from the user screenshot and is not covered by a contract test.
- After: 100% — all OWASP view font resources are resolved from `Application.Current`, matching the existing `Card`, `SignFont`, and `MonoFont` lookups; a contract test prevents regression.

## TDD and verification gates

1. Add a source contract that requires `IconFont` lookup through `Application.Current`; run it red before implementation.
2. Change only the resource-scope lookup in `OwaspCoverageView`.
3. Run the focused Desktop/UI contract tests, JavaScript syntax validation, Release build, and `git diff --check`.
4. Publish the completed-modules self-contained single-file Desktop bundle and verify its manifest and PE header.
5. Record evidence and the authoritative continuation pointer in `handoff.md`.

## Boundary

This module verifies the source/build contract and published bundle. It does not claim authenticated visual smoke testing on a separate Windows machine, Authenticode signing, or hosted CodeQL/SARIF evidence.
