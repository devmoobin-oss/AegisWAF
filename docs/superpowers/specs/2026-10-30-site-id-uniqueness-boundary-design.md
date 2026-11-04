# Module 198: Site ID uniqueness boundary

## Goal

Reject duplicate Site IDs at the typed Multi-Site snapshot boundary before the
registry, route builder, or API summary dictionaries can silently overwrite or
throw on a duplicate key.

## Contract

- Site IDs are compared after the same trim/lowercase normalization used by the
  runtime configuration.
- Duplicate IDs are rejected regardless of whether the sites are enabled or
  disabled.
- A duplicate ID produces a validation error instead of relying on dictionary
  insertion behavior later in the request path.

## Verification boundary

This module proves the typed snapshot validation contract, the Multi-Site source
regression, Release build, and published artifact checks. It does not claim live
PostgreSQL/Browser/EXE smoke, external penetration/load/SLO/SIEM/CodeQL
evidence, or Authenticode signing.
