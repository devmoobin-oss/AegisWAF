# Multi-instance route activation design

## Context

Site mutations are persisted in PostgreSQL and the request-handling process activates its own in-memory `SiteRegistry` and YARP configuration immediately. A second Gateway process currently has no durable change signal, so it can continue serving an old route snapshot until restart.

## Goal

Every Gateway instance that uses the shared PostgreSQL Site store must converge on the committed Site snapshot without a process restart. A mutation notification must be emitted only after the database transaction commits, and a listener must reload the full validated snapshot rather than applying an untrusted partial payload.

## Design

- Add a PostgreSQL `LISTEN`/`NOTIFY` channel dedicated to Site configuration changes.
- Emit a small redacted payload (`operation`, `siteId`, `schema`) inside the mutation transaction with `pg_notify`; PostgreSQL delivers it only after commit.
- Add a hosted listener with a dedicated connection. On notification, it reads all Sites from PostgreSQL, validates the snapshot, then atomically replaces the local registry and YARP configuration through `SiteRuntimeCoordinator`.
- Reconnect with bounded backoff after listener connection loss. A failed reload keeps the last known-good route snapshot and logs a bounded diagnostic.
- Treat the notification payload only as a wake-up hint. Never deserialize routes or upstream addresses from it.
- Skip the listener when database-backed Site mode is disabled or no connection string exists, preserving file/legacy development behavior.

## Security and reliability

- No credentials, upstream URLs, hostnames, or request data are placed in the notification payload.
- The listener uses the configured PostgreSQL role and inherits the existing least-privilege production gate.
- Snapshot validation remains the final authority; invalid persisted data cannot replace the active snapshot.
- `pg_notify` is part of the same mutation transaction, so rolled-back mutations do not trigger a durable activation event.
- Initial load remains the existing startup snapshot; the listener performs a catch-up reload after `LISTEN` so a race during startup converges.

## Non-goals

- No distributed lock or leader election; all instances may reload the same committed snapshot.
- No notification for health-history or acknowledgement-only writes.
- No change to request routing semantics, egress policy, or the admin authorization model.

## Acceptance gates

1. Notification payload is bounded, redacted, and deterministic.
2. Insert/update/delete emit notifications only on commit.
3. Listener reloads and activates a changed snapshot in a peer Gateway without restart.
4. Listener reconnects after a dropped database connection and preserves the last known-good snapshot on invalid reload.
5. Solution and focused regression builds pass with zero warnings/errors.
