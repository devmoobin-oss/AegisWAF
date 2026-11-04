# PostgreSQL Fault-Injection Supervisor Design

## Intent

Provide an explicit, opt-in integration harness for the Multi-Site runtime listener. It must stop and restart PostgreSQL while a real `EmbeddedGateway` is running, verify that readiness degrades during the outage, and verify that the listener converges again after PostgreSQL returns.

## Goals

- Keep the Gateway lifetime in a standalone .NET process instead of an xUnit/TestServer fixture.
- Keep PostgreSQL lifecycle and timeout/cleanup ownership in a PowerShell supervisor.
- Use the unauthenticated `/ready` contract to observe `postgres`, `site-runtime-listener`, and `sites` health without storing or automating a user session.
- Require an explicit opt-in variable and explicit database lifecycle flag; never run as part of the normal test suite.
- Emit machine-readable evidence with observed phases, timestamps, HTTP status/body summaries, and cleanup results.

## Non-goals

- This is not a production outage controller.
- It does not modify production databases; it creates and drops a uniquely named disposable database.
- It does not replace hosted chaos testing, external failover testing, or a service supervisor.

## Safety constraints

- The supervisor refuses to run unless `AEGISWAF_ENABLE_PG_FAULT_INJECTION=1` is set.
- PostgreSQL start/stop is allowed only with `-ManagePostgres` and an explicit `pg_ctl` path/data directory.
- Every phase has a bounded timeout.
- A `finally` block stops the runner and restores PostgreSQL to the state owned by the supervisor.
- Secrets are accepted only through the existing `AEGISWAF_TEST_PG` environment variable and never written to evidence.

## Approaches

1. Extend the existing xUnit/TestServer fault test. Rejected: the prior attempt hung during fixture teardown, so it cannot prove terminal cleanup.
2. Use an external container orchestrator. Rejected for this Windows workspace because it adds an unavailable runtime dependency and would not prove the local PostgreSQL path used by the application.
3. Use a standalone .NET Gateway runner plus PowerShell lifecycle supervisor. Selected: it isolates process lifetime, allows real PostgreSQL stop/start, has bounded polling, and records cleanup independently.

## Acceptance

The module is complete only when an opt-in run records: Gateway ready, outage observed as non-ready with the listener degraded/reconnecting, PostgreSQL restarted, Gateway ready again with the listener healthy, and deterministic cleanup. A local contract/self-test alone is insufficient.

