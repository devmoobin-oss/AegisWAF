# Module 189: Multi-Site persistence null-nested write boundary

## Goal

Prevent malformed in-memory site graphs from escaping the persistence writer as unhandled `NullReferenceException` failures.

## Contract

`SitePersistenceCodec.ToRow` must reject a null upstream element and null Egress collections with `InvalidDataException` before serializing a persistence row. Valid sites retain the existing round-trip behavior.

## Verification

- RED: focused tests demonstrate the unhandled null failure before the guard.
- GREEN: focused persistence tests pass 12/12 after the guard.
- Regression: run the targeted Multi-Site test set.
- Release: build/publish and verify the self-contained artifact and manifest.

## Boundaries

No live PostgreSQL, browser, published-EXE UI, external penetration/load/SLO/SIEM/CodeQL, or Authenticode evidence is claimed by this module.
