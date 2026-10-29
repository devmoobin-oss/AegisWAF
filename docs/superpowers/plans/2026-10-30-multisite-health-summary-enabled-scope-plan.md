# Module 171 — Scope Health Summary to enabled Sites

## Goal

Make the Multi-Site Health Summary represent only routes that are currently enabled. Disabled Site records remain available through the Site CRUD endpoint, but must not contribute to current critical/degraded/healthy health counts or summary pagination.

## Before / after contract

- Before: `/api/sites/health/summary` calls the generic Site summary page and includes `waf_sites` rows with `enabled=false`.
- After: the endpoint calls a dedicated enabled-only summary query. `totalSites`, `returnedSites`, pagination, and alert counts describe monitored enabled Sites only.
- Disabled Site configuration remains visible through `/api/sites` and can still be edited or enabled.

## TDD and verification

1. Add a source contract test that requires the endpoint to call `ListEnabledSummaryPageAsync` and the store query to contain `WHERE enabled`.
2. Run the focused contract test before implementation; it must fail.
3. Implement the dedicated store method and switch the endpoint.
4. Run focused Multi-Site health, summary, and persistence contracts.
5. Run JavaScript syntax validation, Release build, `git diff --check`, and the release publish/manifest verification.
6. Record the exact before/after percentages, test evidence, and known environment boundaries in `handoff.md`.

## Safety boundary

This module changes only the summary monitoring scope. It does not delete disabled configuration, change route activation, alter health probe thresholds, or reinterpret historical observations for enabled Sites.
