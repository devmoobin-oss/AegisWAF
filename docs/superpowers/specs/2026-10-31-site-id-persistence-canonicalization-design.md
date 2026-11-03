# Module 208: Site ID persistence canonicalization

## Goal

Ensure the persistence row written by the Multi-Site storage boundary uses the
same canonical Site ID that runtime lookup and database reads use.

## Contract

- `SitePersistenceCodec.ToRow` trims and lowercases the Site ID before the row
  is handed to PostgreSQL.
- The existing validation still rejects Site IDs that do not satisfy the safe
  Site ID pattern.
- Read behavior remains canonical and unchanged; this closes the write-side
  mismatch that could leave auxiliary persistence keys under a different raw
  representation.

## Verification boundary

This module proves the persistence codec regression, the Multi-Site source
regression, the Release build, and the published artifact checks. It does not
claim live PostgreSQL/Browser/EXE smoke, external penetration/load/SLO/SIEM/
CodeQL evidence, or Authenticode signing.
