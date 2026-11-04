# Module 190: Multi-Site persistence null-row read boundary

## Goal

Make the persistence materialization boundary reject a missing row deterministically instead of dereferencing it and leaking `NullReferenceException`.

## Contract

`SitePersistenceCodec.FromRow` must reject a null `SitePersistenceRow` with `InvalidDataException` before accessing any row field. Valid and malformed non-null rows keep their existing validation behavior.

## Verification

- RED: focused test demonstrates the null-row failure before the guard.
- GREEN: focused persistence tests pass 13/13 after the guard.
- Regression: run the targeted Multi-Site suite.
- Release: build/publish and verify the self-contained artifact and manifest.

## Boundaries

No live PostgreSQL, browser, published-EXE UI, external penetration/load/SLO/SIEM/CodeQL, or Authenticode evidence is claimed by this module.
