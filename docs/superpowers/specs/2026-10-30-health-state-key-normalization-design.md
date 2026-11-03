# Module 202: Health state key normalization

## Goal

Ensure the process-local health registry treats Site IDs and upstream IDs using
the same normalization semantics as the Multi-Site configuration and API.

## Contract

- Site state keys are trimmed and normalized with the shared Site ID rule.
- Upstream state keys are trimmed and compared case-insensitively.
- A health result reported as `PRIMARY` applies to the configured `primary`
  upstream instead of creating an unrelated state entry.

## Verification boundary

This module proves health state key consistency, the Multi-Site source
regression, Release build, and published artifact checks. It does not claim live
PostgreSQL/Browser/EXE smoke, external penetration/load/SLO/SIEM/CodeQL
evidence, or Authenticode signing.
