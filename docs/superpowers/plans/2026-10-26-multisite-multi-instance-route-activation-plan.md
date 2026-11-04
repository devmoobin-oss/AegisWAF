# Multi-instance route activation implementation plan

## Task 1 — notification contract and TDD

- Add tests for bounded/redacted payload parsing and mutation notification behavior.
- Run the new tests before implementation and record the RED result.
- Implement a small notification contract with no route data in the payload.

## Task 2 — transactional mutation notifications

- Emit `pg_notify` from Site insert/update/delete transactions before commit.
- Keep health and acknowledgement writes outside the route-change channel.
- Add PostgreSQL integration coverage that observes committed mutation notifications.

## Task 3 — per-instance listener and peer convergence

- Register a hosted listener only for database-backed Site mode.
- Reload all Sites, validate, and activate atomically on notification; reconnect with bounded backoff.
- Add a real two-Gateway-instance integration test that mutates through one API and observes the peer registry/routes.

## Task 4 — verification and handoff

- Run solution/runner builds with warnings as errors.
- Run focused tests inside and outside the sandbox where socket/database access is required.
- Run `git diff --check`, write a verification artifact, update `handoff.md`, and record the next opportunities.
