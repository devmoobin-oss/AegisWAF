# Module 211 — Transactional Site Mutation Lock

## Opportunity and root cause

`SiteDatabaseStore.AcquireSiteMutationLockAsync` used PostgreSQL's session-level `pg_try_advisory_lock` while connections were pooled. Disposing an `NpgsqlConnection` returns it to the pool without ending the PostgreSQL session, so the advisory lock could remain held by an idle pooled connection. A later Site mutation could then time out with `site_mutation_busy` even though no mutation was active.

## Contract

- The Site mutation lock is acquired with `pg_try_advisory_xact_lock` inside a dedicated PostgreSQL transaction.
- The lock transaction remains alive for the complete caller workflow.
- Disposing the lock lease disposes the transaction before returning the connection to the pool, releasing the lock deterministically.
- Cross-instance serialization, bounded polling, timeout mapping, and the existing lock key remain unchanged.

## Non-goals

- No schema migration is required.
- No change to the mutation workflow, authorization, or runtime activation semantics.

## Verification

- RED: the new pooled-connection release test timed out and lock contract tests rejected the old function.
- GREEN: focused lock tests and the full Site CRUD integration suite pass after the change.
