# Multi-site Listener Readiness — Plan

1. Write RED tests for reconnecting/degraded and disabled/healthy states plus secret-safe descriptions.
2. Implement `SiteRuntimeListenerHealthCheck` and register it in the `ready` health-check set.
3. Extend the real PostgreSQL/TestServer integration to require `/ready` success and expose retention/listener diagnostics.
4. Run the full focused Multi-site/event regression, solution build, and diff checks.
5. Record before/after evidence and audit the next opportunity in `handoff.md`.
