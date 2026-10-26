# Security Event Retention — Design

## Goal

Keep PostgreSQL security-event storage bounded without weakening structured logging or external SIEM delivery. Events older than a configured server-clock retention window are deleted by a hosted cleanup worker.

## Contract

- `Waf:Events:RetentionHours`: 1–8760 hours; default 168 hours.
- `Waf:Events:CleanupIntervalSeconds`: 60–86400 seconds; default 3600 seconds.
- Cleanup uses PostgreSQL `now()` and a parameterized interval, so retention does not depend on the Gateway machine clock.
- `/api/overview.securityEventRetention` reports bounded state, configured window, last cleanup time, deleted count, and failure count.
- Retention is a local bounded store policy, not a replacement for SIEM archival or alert routing.

## Safety

- Production readiness rejects values outside the documented bounds.
- Cleanup deletes only rows older than the retention cutoff and does not touch audit-chain rows.
- Cleanup failures are logged and exposed as `degraded`; the worker does not terminate the Gateway.
- Existing event batching/backpressure and structured logging remain independent paths.
