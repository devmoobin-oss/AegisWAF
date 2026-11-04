# Multi-site Summary Observability Design

## Intent

Give operators enough signal to distinguish a healthy summary refresh, a slow bounded query, and an unavailable gateway without exposing database, host, connection-string, or raw exception details.

## Goals

- Return generated time, elapsed milliseconds, fixed budget, boolean budget result, and a small status (`ready` or `slow`) from the bounded summary endpoint.
- Keep the existing 64-observation/site and 100-site limits.
- Provide stable Desktop loading and error text in the existing summary slot.
- Preserve Site rows and existing management actions during summary failure.

## Non-goals

- Exposing SQL text, database names, hostnames, connection strings, stack traces, or infrastructure topology.
- Adding a background polling loop or changing refresh frequency.
- Treating a slow query as a security failure; it is an operator signal.

## Acceptance gates

- PostgreSQL/API contract verifies timing/status fields and bound metadata.
- UI contract verifies loading, unavailable, and timing/status copy.
- Solution build, focused tests, and full regression pass.
