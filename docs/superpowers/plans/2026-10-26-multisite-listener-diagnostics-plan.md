# Multi-site Listener Diagnostics — Plan

1. Write failing status-transition tests for connect, disconnect, reload failure, disabled, and stopped states.
2. Add a thread-safe redacted status service and update `SiteRuntimeChangeListener` at each lifecycle transition.
3. Register the service and expose its snapshot through `/api/overview`.
4. Add contract and real PostgreSQL/TestServer integration tests for the response shape and secret redaction.
5. Update production-readiness documentation, record before/after evidence in `handoff.md`, and run focused regression, solution build, and diff checks.
