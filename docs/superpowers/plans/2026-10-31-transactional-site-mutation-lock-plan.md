# Module 211 — Transactional Site Mutation Lock Plan

1. Add a contract requiring transaction-level advisory locking and an integration regression that acquires a second pooled connection after the first lease is disposed.
2. Run the focused lock tests and record the expected RED failures.
3. Begin a PostgreSQL transaction before lock polling, use `pg_try_advisory_xact_lock`, and keep the transaction in the lease.
4. Run focused GREEN tests, the complete Site CRUD integration suite, and the Multi-Site source regression.
5. Build and publish the self-contained single-file Windows artifact, verify the manifest/hash/PE header, and update handoff.
