# Module 192: Auth login availability boundary

## Goal

Make the login endpoint return a safe, actionable `503` when authentication persistence is unavailable because of a known PostgreSQL/configuration failure, while preserving request cancellation and unrelated programming failures.

## Contract

- Classify `NpgsqlException`, `TimeoutException`, `InvalidOperationException`, and `ArgumentException` as service-availability failures at the login boundary.
- Do not classify `OperationCanceledException` or unrelated exceptions as service-availability failures.
- Log the exception server-side and return a generic RFC7807 response with a stable `auth_database_unavailable` code; never return connection strings or provider details to the client.

## Verification boundary

This module proves the classifier contract and Release/publish compilation. Live PostgreSQL, authenticated EXE login, browser smoke, external security scans, and Authenticode signing remain separate gates.
