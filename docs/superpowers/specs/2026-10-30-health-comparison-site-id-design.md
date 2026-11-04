# Module 206: Health comparison Site ID normalization

## Goal

Keep expected upstream counts aligned with the same canonical Site ID used by
the configuration and health registries, so comparison alerts do not lose
their completeness context when a runtime Site ID contains surrounding
whitespace.

## Contract

- `SiteHealthComparisonCalculator` normalizes each comparison Site ID before
  looking it up in `expectedUpstreamCounts`.
- The display/output Site ID remains unchanged; only the lookup key is
  canonicalized.
- A missing expected count continues to use the existing fallback behavior.

## Verification boundary

This module proves the focused comparison-alert regression, the Multi-Site
source regression, the Release build, and published artifact checks. It does
not claim live PostgreSQL/Browser/EXE smoke, external penetration/load/SLO/
SIEM/CodeQL evidence, or Authenticode signing.
