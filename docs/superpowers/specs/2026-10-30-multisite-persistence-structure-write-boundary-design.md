# Module 188: Multi-Site persistence structure write boundary

## Goal

Ensure `SitePersistenceCodec.ToRow` cannot serialize a `GatewaySite` whose structural or policy fields would be rejected by the persistence read boundary.

## Contract

Before returning a `SitePersistenceRow`, the write path must enforce the same persistence invariants as `FromRow`: required collections and policies are present, site collections and upstream IDs are non-empty and unique, health/rate/egress values are within bounds, and the row version is valid.

The write path must fail with `InvalidDataException` before handing the row to a repository. Valid sites must keep the existing round-trip behavior.

## Verification

- RED: add a focused structural test covering empty/duplicate collections and invalid health, rate, egress, and version values.
- GREEN: validate the candidate row through the shared persistence read boundary after safe construction; focused persistence tests must pass 11/11.
- Regression: run the targeted Multi-Site suite.
- Release: build and publish the self-contained Windows artifact, then verify its manifest and hash.

## Boundaries

This module does not claim live PostgreSQL, browser, published-EXE UI, external penetration/load/SLO/SIEM/CodeQL, or Authenticode evidence.
