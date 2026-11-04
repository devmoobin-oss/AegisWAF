# Module 209: Canonical Site ID for hostname rows

## Goal

Keep the auxiliary `waf_site_hostnames` table keyed by the same canonical Site
ID as the primary `waf_sites` row during create, update, and seed workflows.

## Contract

- `ReplaceHostnamesAsync` normalizes `site.Id` once before deleting old rows
  and inserting current hostname rows.
- Main Site persistence and hostname-row persistence therefore use the same
  Site ID identity even when a direct internal caller supplies whitespace or
  case variants.

## Verification boundary

This module proves the source contract and Multi-Site regression, the Release
build, and the published artifact checks. It does not claim live PostgreSQL/
Browser/EXE smoke, external penetration/load/SLO/SIEM/CodeQL evidence, or
Authenticode signing.
