# Module 164 — Surface Multi-Site database bootstrap failures

## Goal

Prevent `SiteDatabaseStore.TryLoadSnapshot` from silently treating PostgreSQL connectivity/authentication failures as an empty snapshot. A desktop Gateway must fail during startup with the database error so the login window does not misleadingly report only `Authentication service unavailable`.

## TDD evidence

1. Add a regression test using an unreachable PostgreSQL endpoint.
2. Run the focused test and confirm it fails because the current implementation swallows `NpgsqlException`.
3. Remove only the broad `NpgsqlException` catch; keep the `UndefinedTable` compatibility path for first-start migration ordering.
4. Re-run focused, related, build, syntax, manifest, and published artifact checks.

## Boundaries

- Preserve `null` for an absent connection string and the expected `UndefinedTable` first-start case.
- Do not expose database credentials in diagnostics.
- Do not alter authentication status codes or CRUD behavior in this module.
