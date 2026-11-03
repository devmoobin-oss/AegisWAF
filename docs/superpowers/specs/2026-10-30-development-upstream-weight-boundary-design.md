# Module 197: Development upstream-weight boundary

## Goal

Keep file-based Multi-Site configuration consistent with the API and persistence
boundaries by rejecting an invalid upstream weight before binding a runtime site.

## Contract

- Upstream weights must be integers from 1 through 10,000 in every deployment mode.
- Development mode must not silently clamp invalid weights.
- `SiteConfigurationLoader` must not bind a value that the validator would reject.
- Valid omitted weights continue to use the default value of 100.

## Verification boundary

This module proves the configuration validator/loader contract, the Multi-Site
source regression, Release build, and published artifact checks. It does not
claim live PostgreSQL/Browser/EXE smoke, external penetration/load/SLO/SIEM/
CodeQL evidence, or Authenticode signing.
