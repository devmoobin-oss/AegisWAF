# Published EXE UI Smoke Design

## Goal

Verify that the exact published desktop artifact can launch on Windows, create its main window, and start the embedded Gateway before authentication.

## Scope

- Artifact: `dist/AegisWAF-Desktop/AegisWAF.exe`
- Verify the published file hash and size against `release-manifest.json`.
- Launch the executable with PostgreSQL available.
- Confirm the visible window title is `AegisWAF — เข้าสู่ระบบ`.
- Confirm the login window reports that the protection system is ready.
- Do not enter credentials or automate authentication; Multi-Site navigation after login requires an operator to take over.
- Close the app and stop the temporary PostgreSQL service after the smoke test.

## Acceptance

The module is complete when the artifact opens, the embedded Gateway reaches the login-ready state, the post-test cleanup is verified, and the evidence records the authentication boundary honestly.

