# Module 201: Health weighted-routing boundary

## Goal

Ensure the process-local health weighted round-robin honors every upstream
weight accepted by the Multi-Site configuration, persistence, and API
boundaries.

## Contract

- The accepted upstream weight range is 1 through 10,000.
- Health selection must not silently reduce valid weights above 1,000.
- Configuration, API, persistence, and health selection use one shared maximum.

## Verification boundary

This module proves the weight-boundary consistency and selection behavior, the
Multi-Site source regression, Release build, and published artifact checks. It
does not claim live PostgreSQL/Browser/EXE smoke, external
penetration/load/SLO/SIEM/CodeQL evidence, or Authenticode signing.
