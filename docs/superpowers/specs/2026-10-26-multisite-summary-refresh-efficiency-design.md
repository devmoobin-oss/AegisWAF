# Multi-site Summary Refresh Efficiency Design

## Intent

The Desktop Multi-site screen currently loads one trend request per Site and then loads the cross-site summary again. Use the already bounded summary projection as the single refresh source to remove the N+1 request pattern.

## Goals

- Refresh the Site grid with one Sites request and one bounded summary request.
- Map summary rows to Site rows without exposing new sensitive fields.
- Preserve explicit `ไม่มีข้อมูล` fallback when the summary endpoint is unavailable.
- Keep edit/delete/health-check actions unchanged.

## Non-goals

- Changing server retention, alert semantics, or database schema.
- Adding an unbounded client cache or background polling loop.

## Acceptance gates

- Desktop contract proves the refresh path consumes `summary.Sites` and no longer calls per-Site trend requests.
- Existing UI contract, solution build, focused tests, and full regression pass.
