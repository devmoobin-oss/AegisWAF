# Module 200: Site registry hostname duplicate guard

## Goal

Make the runtime Site registry fail closed when a malformed snapshot contains
duplicate active hostnames, instead of silently routing the hostname to the
first site inserted into the index.

## Contract

- Hostname keys use the shared hostname normalization.
- Duplicate normalized hostnames raise an explicit `InvalidDataException`.
- The registry never silently chooses an owner for an ambiguous active hostname.

## Verification boundary

This module proves the registry hostname-index contract, the Multi-Site source
regression, Release build, and published artifact checks. It does not claim live
PostgreSQL/Browser/EXE smoke, external penetration/load/SLO/SIEM/CodeQL
evidence, or Authenticode signing.
