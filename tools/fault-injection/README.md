# PostgreSQL fault-injection supervisor

This is an explicit, destructive local integration test for the Multi-Site PostgreSQL runtime listener. It is intentionally not part of the normal xUnit suite because it owns PostgreSQL and must have deterministic process cleanup.

## Safety

The supervisor refuses to run unless both conditions are present:

```powershell
$env:AEGISWAF_ENABLE_PG_FAULT_INJECTION = '1'
```

and the `-ManagePostgres` switch is supplied. It creates a uniquely named disposable database using `AEGISWAF_TEST_PG`, then drops it when the runner exits.

## Self-test (does not touch PostgreSQL)

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\fault-injection\test-fault-injection-supervisor.ps1
```

## Real run

Build the Gateway and runner first. The runner intentionally consumes the exact built Gateway DLL at this process boundary:

```powershell
dotnet build .\src\AegisWAF.Gateway\AegisWAF.Gateway.csproj --no-restore
dotnet build .\tools\fault-injection\SiteRuntimeFaultInjectionRunner.csproj --no-restore
```

Only run against a local disposable PostgreSQL instance:

```powershell
$env:AEGISWAF_ENABLE_PG_FAULT_INJECTION = '1'
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\fault-injection\Invoke-SiteRuntimeFaultInjection.ps1 `
  -ManagePostgres `
  -PgCtlPath 'C:\Program Files\PostgreSQL\18\bin\pg_ctl.exe' `
  -DataDirectory 'C:\Program Files\PostgreSQL\18\data'
```

The evidence records ready → degraded/reconnecting → ready, redacted health observations, child-process cleanup, disposable database cleanup, and final PostgreSQL state. A local pass is not hosted chaos or external failover evidence.

`/ready` returns a bounded JSON payload with the overall status and safe per-check statuses. It does not serialize exception messages, connection strings, passwords, SQL, or host details. The supervisor evaluates `postgres` and `site-runtime-listener` independently so an unrelated synthetic upstream check cannot hide listener recovery. The Windows PowerShell supervisor uses `curl.exe` with a bounded timeout and PID polling for `pg_ctl`; it does not depend on PowerShell 7-only `-SkipHttpErrorCheck` or newer .NET hash APIs.
