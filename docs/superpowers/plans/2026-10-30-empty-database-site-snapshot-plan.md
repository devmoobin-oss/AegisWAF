# Module 165 — Preserve an explicitly empty database Site snapshot

## Goal

When the `waf_sites` table exists and contains zero rows, treat that result as the authoritative Multi-Site state. Do not fall back to configuration Sites and re-seed a Site that an operator deliberately deleted.

## TDD evidence

1. Add a regression contract for the empty-table result.
2. Run it and confirm it fails while `TryLoadSnapshot` returns `null` for zero rows.
3. Change the successful table-read path to return an empty `SiteConfigurationSnapshot`; keep `null` only for missing connection configuration or the intentional `UndefinedTable` first-start migration path.
4. Re-run focused, related, build, syntax, manifest, and published artifact checks.

## Boundaries

- Preserve configuration fallback when the database table has not been created yet.
- Preserve production validation that rejects zero active Sites.
- Live PostgreSQL empty-table execution remains an external verification boundary when PostgreSQL is unavailable.
