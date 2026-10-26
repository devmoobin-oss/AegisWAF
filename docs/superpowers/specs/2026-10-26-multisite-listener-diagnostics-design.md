# Multi-site Listener Diagnostics — Design

## Goal

Make PostgreSQL-backed Multi-site convergence diagnosable without exposing connection strings or raw database errors. Operators must be able to distinguish a connected listener, a reconnecting listener, a disabled listener, and a stopped listener from the existing overview response.

## Contract

`GET /api/overview` exposes a `siteRuntimeListener` object containing:

- `state`: `starting`, `connecting`, `connected`, `reconnecting`, `disabled`, or `stopped`
- `isConnected`
- bounded `connectionAttempts` and `consecutiveFailures`
- UTC timestamps for last connection, disconnection, and successful snapshot reload
- `lastErrorType` only; never exception messages, hostnames, credentials, SQL, or connection strings

This state is diagnostic only. `/ready` and per-Site health remain authoritative for readiness and upstream health.

## Safety

- State updates are lock-protected and safe for concurrent hosted-service/API reads.
- Error text is reduced to a bounded exception type name.
- Invalid snapshot reloads preserve the last-known-good route snapshot while retaining a diagnostic error type.
- No configuration secret is copied into the status object or response.
