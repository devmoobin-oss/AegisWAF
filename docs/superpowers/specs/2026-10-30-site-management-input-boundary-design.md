# Module 196: Site-management input boundary

## Goal

Make the Multi-Site create/update boundary fail closed for malformed Hostname and
Egress values instead of silently filtering invalid entries and saving a partial
configuration.

## Contract

- Every submitted Hostname must normalize to a valid hostname; invalid entries
  return a validation error.
- Explicit Egress hosts must be valid, explicit, and unique hostnames; wildcard
  or malformed values are rejected.
- Explicit Egress ports must be unique integers from 1 through 65535.
- Explicit Egress schemes must be unique `http` or `https` values.
- Empty Egress lists continue to derive from the submitted upstreams.
- Valid existing Multi-Site normalization and production validation behavior is
  preserved.

## Verification boundary

This module proves the management-input contract, source-level Multi-Site
regression, Release build, and published artifact checks. It does not claim a
live PostgreSQL/Browser/EXE smoke test, external penetration/load/SLO/SIEM/
CodeQL evidence, or Authenticode signing.
