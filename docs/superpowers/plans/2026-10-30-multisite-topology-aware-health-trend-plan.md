# Multi-Site topology-aware health trend plan

## Goal

Prevent historical health observations for removed or renamed upstreams from keeping the current Site in `degraded` or `critical` state after a Multi-Site topology change.

## Scope

- Keep the complete PostgreSQL health history available for audit/history screens.
- Filter only trend, acknowledgement, and summary calculations to the Site's current upstream IDs.
- Read the bounded retention window before filtering so recent observations for current upstreams are not hidden by retired upstream rows.
- Preserve existing alert semantics for unchanged topology.

## Verification gates

1. Add a failing regression test proving a retired upstream cannot affect the current Site trend.
2. Add source contracts for direct trend, acknowledgement, and summary paths.
3. Run the focused health-trend suite and the related Multi-Site regression suite.
4. Run Release build, JavaScript syntax check, and `git diff --check`.
5. Publish the completed-modules self-contained executable and verify its manifest/hash.

## Boundary

Live PostgreSQL topology-change execution is attempted when the environment is available; if PostgreSQL is not listening, the evidence must record that boundary rather than claim live verification.
