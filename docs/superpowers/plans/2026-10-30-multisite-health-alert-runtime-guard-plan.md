# Module 177 — Multi-Site health alert runtime guard

## Objective

Ensure alert acknowledgement cannot be applied to a Site whose persisted configuration is not active in the current Gateway runtime.

## Scope

- Centralize the persisted-versus-active Site comparison used to identify `pending-restart` state.
- Apply the comparison to Site listing, manual health checks, and alert acknowledgement.
- Return a stable `site_runtime_pending_restart` error with actionable Thai copy for Browser and Desktop clients.
- Preserve the existing role, CSRF, optimistic concurrency, and PostgreSQL write requirements.

## Verification

- TDD red: runtime-state and acknowledgement contract tests must fail before the shared guard and endpoint enforcement exist.
- TDD green: focused runtime/health regression, JavaScript syntax check, Release build, and diff whitespace check.
- Publish the completed-modules bundle and verify its release manifest and SHA-256.
- Record before/after progress and evidence in `handoff.md`.

## Boundary

This module prevents an invalid acknowledgement action; it does not automatically activate, mutate, or delete Site configuration.
