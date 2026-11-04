# Module 22 implementation plan

1. Add failing tests for the five-minute server timestamp freshness policy, future/missing timestamp handling, and keyboard-accessible recovery details.
2. Implement deterministic freshness helpers in the shared recovery state.
3. Render age/stale status and place bounded events in an accessible Desktop Expander without issuing requests.
4. Run focused recovery/UI/database tests, solution build, and diff verification.
5. Record evidence and perform the next opportunity audit in `handoff.md`.
