# Module 204: Health trend upstream filter normalization

## Goal

Ensure trend topology filtering compares current upstream IDs using normalized
values, so valid observations are not discarded because of whitespace or casing.

## Contract

- Current upstream IDs are trimmed before entering the filter set.
- Observation upstream IDs are trimmed before lookup.
- Equivalent IDs such as `primary` and ` PRIMARY ` remain in the trend.

## Verification boundary

This module proves trend topology filtering, the Multi-Site source regression,
Release build, and published artifact checks. It does not claim live
PostgreSQL/Browser/EXE smoke, external penetration/load/SLO/SIEM/CodeQL
evidence, or Authenticode signing.
