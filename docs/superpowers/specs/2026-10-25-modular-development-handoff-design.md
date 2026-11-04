# Modular Development and Handoff Design

## Intent

AegisWAF already contains several independently testable areas, but progress is currently spread across roadmap documents and uncommitted UI work. This design establishes a repeatable module ledger so every development round starts from `handoff.md`, records a measured before/after percentage, and only reports a module above 98% when its gates have fresh evidence.

## Goals

- Keep six stable module boundaries: WAF engine, Gateway/perimeter, authentication/RBAC/audit, Client/Desktop UI, database/operations, and documentation/verification.
- Record each module's baseline, changes, evidence, remaining risks, and next action in `handoff.md`.
- Use build, test, and runtime evidence rather than estimates to change percentages.
- Preserve existing user changes in the current worktree.
- Make verification commands deterministic on this Windows/.NET 10 environment.

## Non-goals

- Claim independent penetration-test certification or OWASP compliance.
- Replace the existing WAF architecture or introduce a new deployment model in this pass.
- Remove or reset existing Desktop UI changes.
- Treat a passing unit test as proof of external security assurance.

## Approaches

1. **Manual-only ledger:** fastest to start, but percentages would drift and future rounds would repeat the same checks.
2. **Script-driven evidence ledger:** keep `handoff.md` as the human-readable record, while a small PowerShell verifier runs the project gates, emits machine-readable evidence, and updates the relevant module row. This fits the existing Windows tooling and keeps the workflow reproducible.
3. **CI-only ledger:** strongest for shared branches, but does not help the local development loop and cannot verify the WPF runtime without a Windows session.

The selected approach is **script-driven evidence ledger**. Local verification is the primary loop; CI/external testing remains a separate gate. Percentages use a fixed checklist of implementation, automated test, runtime check, and documentation evidence. A module cannot exceed 98% when a required gate is missing or an important risk is unresolved.

## Module gates

| Module | Required evidence |
|---|---|
| WAF inspection engine | Gateway build, engine regression tests, catalog claim tests, normalization/body/response-path checks |
| Gateway / perimeter security | Gateway build, health/readiness and security middleware tests, configuration/limits checks |
| Authentication / RBAC / audit | Auth/RBAC build and tests, authorization matrix, CSRF/session/audit integrity checks |
| Client API / Desktop UI | Client/Gateway/Desktop builds, XAML resource contract, UI smoke check where available |
| Database / migrations / operations | migration/build checks, readiness and distributed-control tests, safe load-test harness evidence |
| Documentation / handoff / verification | this design, plan, reproducible verifier, handoff history and completion audit |

## Percentage rule

Each module has four equally weighted gates: implementation, automated tests, runtime/integration evidence, and documentation/operational evidence. The displayed percentage is the sum of passed gate weights, with partial gate values only when a gate has explicit test evidence. An environment failure is recorded as unverified, never converted into a passing percentage.

## Constraints and decisions

- Use `--disable-build-servers -m:1` for .NET verification in this sandbox because parallel MSBuild/testhost named-pipe behavior is unreliable.
- Do not change or delete unrelated user worktree changes.
- Keep security limitations visible in `handoff.md` and the existing security documentation.
- Fix defects with a regression test or a reproducible build gate before changing the percentage.
