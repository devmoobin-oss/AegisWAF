# Module 207: Upstream ID boundary and identity

## Goal

Make Upstream IDs safe and canonical across file configuration, API mutation,
health selection, and persistence so runtime/YARP keys cannot receive
arbitrary input and equivalent IDs do not split health or database identity.

## Contract

- Upstream IDs normalize by trim + lowercase and must match the safe runtime
  key pattern `[a-z0-9][a-z0-9_-]{0,63}`.
- File configuration supports an explicit `Id` field for indexed upstream
  entries and normalizes the resolved ID before constructing `SiteUpstream`.
- API and persistence boundaries reject unsafe IDs; persistence writes
  canonical IDs and reads them back canonically.
- Health selection normalizes the SiteUpstream ID before looking up process
  local health state.

## Verification boundary

This module proves the focused boundary and identity tests, the Multi-Site
source regression, the Release build, and the published artifact checks. It
does not claim live PostgreSQL/Browser/EXE smoke, external penetration/load/
SLO/SIEM/CodeQL evidence, or Authenticode signing.
