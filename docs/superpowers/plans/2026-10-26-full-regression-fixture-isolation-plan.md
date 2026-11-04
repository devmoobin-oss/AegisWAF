# Full Regression Fixture Isolation Plan

## Step 1 — Reproduce and diagnose

- Run the full suite under an execution context that permits loopback.
- Capture response-inspection logs and readiness status.
- Confirm whether failures originate in production code or fixture lifecycle/routing setup.

## Step 2 — Apply minimal test isolation fixes

- Set `PreferConfigurationSites: true` only in `EngineUpgradeTests.ResponseInspection`.
- Replace the one-shot `/ready` assertion with a bounded retry loop in `SiteRuntimeListenerStatusIntegrationTests`.
- Keep all production routing, health, and readiness code unchanged.

## Step 3 — Verify

- Rebuild the test project so the changed tests are executed.
- Run the 13-test focused gate.
- Run the full `dotnet test` suite and require exact counts of 494 passed, 0 failed, 0 skipped.
- Run `Invoke-FullDotnetRegression.ps1` to create machine-readable evidence.
- Run `git diff --check` and record the evidence in `HANDOFF.md`.
