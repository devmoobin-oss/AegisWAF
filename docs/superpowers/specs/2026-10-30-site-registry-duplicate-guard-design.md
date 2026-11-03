# Module 199: Site registry duplicate guard

## Goal

Make the runtime Site registry fail closed when it receives a malformed snapshot
with duplicate IDs, instead of silently keeping the first dictionary entry.

## Contract

- Registry keys use the same trim/lowercase normalization as configuration.
- A duplicate normalized Site ID raises an explicit `InvalidDataException`.
- The registry never silently drops one of two sites with the same runtime key.

## Verification boundary

This module proves the registry construction contract, the Multi-Site source
regression, Release build, and published artifact checks. It does not claim live
PostgreSQL/Browser/EXE smoke, external penetration/load/SLO/SIEM/CodeQL
evidence, or Authenticode signing.
