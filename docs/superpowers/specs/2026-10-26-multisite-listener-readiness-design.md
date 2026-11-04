# Multi-site Listener Readiness — Design

## Goal

Make `/ready` reflect the real state of the PostgreSQL Site-runtime listener so an instance is not advertised as fully ready while its route-convergence listener is reconnecting or stopped.

## Contract

- Database-backed mode with a connected listener is `Healthy`.
- `starting`, `connecting`, or `reconnecting` is `Degraded` and does not expose database details.
- A stopped listener is `Unhealthy`.
- Configuration-sites mode or no configured database keeps this check `Healthy` because the listener is intentionally disabled.
- PostgreSQL connectivity and per-Site/YARP destination checks remain independent readiness checks.

## Safety

- Health descriptions are fixed generic text; no connection string, hostname, password, SQL, or raw exception is returned.
- The check consumes the thread-safe status snapshot and performs no database I/O.
- The listener status remains diagnostic; readiness does not replace per-Site upstream health.
