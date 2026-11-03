# Module 205: Health trend upstream identity

## Goal

Ensure trend completeness and recovery calculations treat equivalent upstream
IDs as one identity, even when persisted observations contain whitespace.

## Contract

- Upstream IDs are trimmed before distinct counting in trend calculations.
- Batch completeness cannot be satisfied by duplicate representations of one
  upstream.
- Historical failure and latest healthy comparisons use the same normalized
  identity.

## Verification boundary

This module proves trend identity/completeness behavior, the Multi-Site source
regression, Release build, and published artifact checks. It does not claim live
PostgreSQL/Browser/EXE smoke, external penetration/load/SLO/SIEM/CodeQL
evidence, or Authenticode signing.
