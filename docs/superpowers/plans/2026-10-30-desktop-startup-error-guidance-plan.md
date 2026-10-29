# Module 174 — Desktop startup error guidance

## Goal

Turn common embedded-Gateway startup failures into actionable Thai guidance without hiding the original diagnostic detail.

## Before / after

- Before: 0% — `LoginWindow` displayed raw exception text for migration failures such as PostgreSQL `23505` / `could not create unique index`, which made duplicate active Site hostnames look like an authentication failure.
- After: 100% — a client-side classifier maps known startup failures to concise Thai guidance and appends the original exception text for diagnostics; unknown failures preserve the original message.

## Verification gates

1. Add classifier tests and confirm the duplicate-hostname scenario fails before implementation.
2. Implement the classifier and route embedded startup errors through it from `LoginWindow`.
3. Run focused classifier/UI contracts, JavaScript syntax validation, Release build, and `git diff --check`.
4. Publish the completed-modules bundle and verify the release manifest, hash, and PE header.
5. Record evidence and the authoritative continuation pointer in `handoff.md`.

## Boundary

This module improves error diagnosis and verifies source/build behavior. It does not automatically modify or delete conflicting database Sites, and it does not claim authenticated visual smoke testing, Authenticode signing, or hosted CodeQL/SARIF evidence.
