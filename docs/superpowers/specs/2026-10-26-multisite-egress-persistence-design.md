# Multi-site Egress Exception Persistence Design

## Intent

Keep the reviewed non-production `AllowPrivateExceptions` policy stable when a database-backed Site is saved, listed, or loaded after restart. The current model and static configuration already support the flag, but the database codec omitted it.

## Approach

Use one additive PostgreSQL migration with a safe `false` default, then carry the field through `SitePersistenceRow`, `SitePersistenceCodec`, and every `SiteDatabaseStore` SQL read/write path. This keeps the existing database-authoritative architecture and avoids exposing a production-only escape hatch through the management API.

## Security constraints

- Existing rows must remain denied by default.
- Production readiness must continue rejecting the flag.
- The value may be true only when an explicitly reviewed non-production configuration constructs the Site.

## Acceptance

Codec round-trip, PostgreSQL CRUD round-trip, startup snapshot loading, production readiness rejection, and full regression must pass with zero build warnings/errors.
