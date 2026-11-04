# Module 176 — Multi-Site health alert acknowledgement fallback

## Objective

Make alert acknowledgement use the same bounded process-memory fallback as the Health Trend endpoint when a recent probe has not yet been persisted to PostgreSQL.

## Scope

- Merge durable health observations with the bounded process-local observations before calculating the acknowledgement decision.
- Preserve de-duplication, current-upstream filtering, alert thresholds, stale handling, and the existing write/authorization boundary.
- Keep persistence failures explicit; this module does not allow acknowledgement writes without a working database.

## Verification

- TDD red: the contract must fail before the acknowledgement endpoint reads and merges process-memory observations.
- TDD green: focused health/acknowledgement regression, JavaScript syntax check, Release build, and diff whitespace check.
- Publish the completed-modules bundle and verify its release manifest and SHA-256.
- Record before/after progress and evidence in `handoff.md`.

## Boundary

This module fixes a decision mismatch between recent in-memory probes and persisted history. It does not bypass PostgreSQL for acknowledgement writes and does not change role or CSRF enforcement.
