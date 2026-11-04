# Module 173 — Desktop dynamic-view resource scope

## Goal

Prevent dynamic Desktop views from failing when they resolve application-level styles before the view is attached to the window tree.

## Before / after

- Before: 0% — `DashboardView` and `ProtectionView` call `FindResource` on their `UserControl` while constructing dynamic children. This is the same lookup-scope failure pattern that caused the OWASP `IconFont resource not found` error.
- After: 100% — dynamic style lookups use `Application.Current.FindResource`, and a contract test covers both views.

## Verification gates

1. Add the contract test and confirm it fails against the direct `UserControl.FindResource` calls.
2. Replace only the affected style lookups with application-scoped lookups.
3. Run focused UI contracts, JavaScript syntax validation, Release build, and `git diff --check`.
4. Record the module evidence and authoritative continuation pointer in `handoff.md`.

## Boundary

This module verifies source/build behavior. It does not claim authenticated visual smoke testing on a separate machine, Authenticode signing, or hosted CodeQL/SARIF evidence.
