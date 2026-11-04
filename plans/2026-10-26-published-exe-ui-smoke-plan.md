# Published EXE UI Smoke Plan

1. Read the current `handoff.md` state and select the exact published bundle.
2. Verify the executable hash/length and manifest before launching.
3. Start PostgreSQL only for the test window.
4. Launch the exact `.exe` and inspect the returned Windows UI state.
5. Record the login-ready status without submitting credentials.
6. Close the app, stop PostgreSQL, and verify both are no longer running.
7. Write before/after/target progress and remaining authentication-boundary evidence to `handoff.md`.

