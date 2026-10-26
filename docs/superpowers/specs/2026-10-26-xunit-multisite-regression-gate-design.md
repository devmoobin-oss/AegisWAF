# xUnit Multi-Site regression gate

## Problem

The test project previously appeared to fail during SDK/project-reference evaluation and the normal VSTest host later failed to connect. This prevented the Multi-Site health and UI contracts from being verified as real xUnit tests.

## Goal

Restore a deterministic xUnit regression path that builds the test assembly and runs the relevant classes in-process, including loopback probe tests when the environment permits loopback sockets.

## Contract

- `AegisWAF.Tests.csproj` builds successfully with the current .NET SDK after an explicit restore using the repository's local/offline package cache or approved NuGet access.
- The in-process runner discovers the requested classes and fails if any test fails or is missing.
- The focused Multi-Site gate covers `UiContractTests`, `SiteHealthTrendTests`, `SiteHealthProbeTests`, and `HealthResponseWriterTests`.
- The gate must report exact discovered, passed, failed, and expected counts.

## Boundary

The normal VSTest host connection issue is recorded separately; the in-process xUnit runner is the deterministic gate for this environment and does not claim that the VSTest host is fixed.
