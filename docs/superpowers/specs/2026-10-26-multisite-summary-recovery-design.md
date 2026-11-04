# Multi-site Summary Failure Recovery Design

## Intent

Make summary failures recoverable for operators without hiding Sites or creating uncontrolled retry traffic. The gateway exposes only bounded counters; the Desktop offers one explicit retry action.

## Goals

- Track total and consecutive summary failures in process memory without Site IDs, SQL, hosts, or exception text.
- Reset consecutive failures after a successful summary while retaining a bounded numeric total.
- Show one stable retry affordance on the Desktop when summary loading fails.
- Disable the retry button while a refresh is in progress; no automatic retry or polling loop.
- Preserve Site rows and existing management actions on failure.

## Non-goals

- Persisting failure telemetry in PostgreSQL.
- Automatic exponential retry, notifications, or external telemetry.
- Exposing infrastructure or exception details to the client.

## Acceptance gates

- Pure telemetry tests prove increment/reset/bounded behavior.
- API success contract exposes only numeric failure counters.
- Desktop contract proves retry visibility/click path and loading disable behavior.
- Build, focused tests, and full regression pass.
