# Module 21 implementation plan

1. Add failing tests for deterministic stale threshold, bounded event history, success recovery, and UI contract strings.
2. Implement the shared client-side recovery state with numeric-only bounded events.
3. Integrate the state into `SitesView` so failed refreshes can retain and label the last summary while keeping the manual retry affordance.
4. Run focused tests, solution build, and the full PostgreSQL regression runner.
5. Record the evidence artifact and update `handoff.md` before the next opportunity audit.
