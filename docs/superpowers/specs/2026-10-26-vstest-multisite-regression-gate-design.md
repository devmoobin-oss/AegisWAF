# Standard VSTest Multi-Site regression gate

## Problem

The focused xUnit tests pass through the in-process runner, but a plain `dotnet test` can wait for the VSTest testhost when loopback is blocked by the execution environment. That turns an environment limitation into a 90-second hang and obscures whether the test suite itself is healthy.

## Goal

Provide a repeatable standard VSTest gate for the four focused Multi-Site test classes with an explicit timeout, exact test counts, and a separate environment-blocked outcome.

## Contract

- The gate runs `UiContractTests`, `SiteHealthTrendTests`, `SiteHealthProbeTests`, and `HealthResponseWriterTests`.
- Success requires exactly 27 total tests, 27 passed, and 0 failed.
- The gate is bounded and records stdout/stderr tails plus exit status in JSON evidence.
- Loopback/testhost connection limits are reported as `environmentBlocked`, not as a false test failure and not as an indefinite hang.

## Boundary

The wrapper does not weaken test assertions, change application behavior, or bypass the test adapter. It only makes the existing standard VSTest command observable and bounded.
