# Multi-site Egress Exception Persistence Plan

1. Add a migration with `egress_allow_private_exceptions boolean NOT NULL DEFAULT false`.
2. Add a failing codec round-trip test and a PostgreSQL round-trip assertion with the flag enabled.
3. Add the field to persistence row conversion and database SELECT/INSERT/UPDATE/read paths.
4. Run codec, PostgreSQL CRUD, production readiness/egress, and full regression gates.
5. Record before/after percentage and evidence in `handoff.md` and the module artifact.
