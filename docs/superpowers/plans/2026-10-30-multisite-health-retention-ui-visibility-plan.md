# Multi-Site health retention UI visibility

## Opportunity

The API already distinguishes durable PostgreSQL history from process-memory fallback, but the Browser Multi-Site page and WPF Desktop Summary did not surface that boundary. Operators could read a ready-looking health summary without knowing that some history was not durable.

## Module 170 scope

- Show an inline, screen-reader-readable warning in the Browser Multi-Site page when Summary history is process-memory-only or partially durable.
- Show the same boundary in the WPF Desktop Multi-Site Summary text.
- Use the existing operational dark palette and amber warning semantics; do not reuse the red Critical/attack color.
- Preserve loading, unavailable, truncated, stale, and runtime health behavior.
- Keep the API contract and retention vocabulary unchanged.

## TDD and verification

1. Add Browser and Desktop retention-visibility contracts and confirm both fail before the UI change.
2. Implement the shared retention mapping on both operator surfaces.
3. Run focused UI/health contracts, `node --check`, `git diff --check`, and the combined Multi-Site regression.
4. Run the strict premium UI audit and record existing dynamic-button detector findings as a boundary when they are outside this change.
5. Build Release with zero warnings/errors and publish from a clean source commit.

## Completion

Module 170 is complete at 100% when the retention warning is present and localized on both surfaces, tests/build pass, and the release manifest is verified. Existing dynamic-button static-audit findings and live authenticated browser/published-EXE smoke remain separate gates.
