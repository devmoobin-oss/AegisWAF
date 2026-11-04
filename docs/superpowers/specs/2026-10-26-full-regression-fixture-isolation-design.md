# Full Regression Fixture Isolation Design

## Context

The full .NET regression suite exposed four failures even though the production routing and readiness behavior were correct. Three response-inspection tests started an embedded gateway with a scripted upstream, but a persisted database Site snapshot took precedence over the legacy configuration override and routed requests to `localhost:8080`. One listener-status test checked `/ready` only once while the listener was still connecting.

## Goal

Make the regression suite deterministic without weakening production behavior or changing the meaning of readiness states.

## Design

1. Tests that intentionally exercise a configuration-defined scripted upstream must set `PreferConfigurationSites: true`. This keeps the database-backed Multi-Site default for normal integration tests while making the fixture's routing source explicit.
2. The listener-status test must poll `/ready` for a bounded five-second window. A transient 503 during listener startup remains a valid production response; the test waits for the documented eventual-ready state and reports the last status on timeout.
3. A repeatable full-suite wrapper must enforce a timeout, parse exact `Total`, `Passed`, `Failed`, and `Skipped` counts, and distinguish an execution/environment block from a test failure.

## Non-goals

- Do not make a test-only upstream override win over database-backed Sites in production.
- Do not turn `/ready` into a liveness-only endpoint or hide degraded listener state.
- Do not suppress or reinterpret real test failures.

## Acceptance criteria

- Focused response/readiness tests pass 13/13.
- Full suite passes 494/494 with zero failed and zero skipped.
- The bounded wrapper records evidence and returns non-zero for failures or timeout.
