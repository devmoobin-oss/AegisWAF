# Module 203: Health history key normalization

## Goal

Ensure process-local health history and pending-persistence tracking use the
same normalized Site ID semantics as the health registry and Multi-Site API.

## Contract

- History buckets are keyed by trimmed, normalized Site IDs.
- `Record`, `Read`, `HasPending`, and `MarkPersisted` address the same bucket
  for equivalent Site ID representations.
- Whitespace or casing differences cannot create duplicate history buckets or
  leave pending observations unacknowledged.

## Verification boundary

This module proves history/pending key consistency, the Multi-Site source
regression, Release build, and published artifact checks. It does not claim live
PostgreSQL/Browser/EXE smoke, external penetration/load/SLO/SIEM/CodeQL
evidence, or Authenticode signing.
