# Security Event Retention — Plan

1. Add RED tests for bounded retention options, production validation, and old/new row behavior.
2. Implement a parameterized PostgreSQL cleanup worker with server-clock cutoff and bounded status counters.
3. Register the worker and expose its redacted status through `/api/overview`.
4. Run pure, contract, PostgreSQL integration, focused regression, solution build, and diff checks.
5. Record before/after evidence and the next opportunity in `handoff.md`.
