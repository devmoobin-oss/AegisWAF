# AegisWAF security/load-test harness

This harness is for a local synthetic upstream or an explicitly authorized test environment. It is not a penetration-testing tool and does not send destructive payloads.

Example:

```powershell
.\loadtest.ps1 -TargetUri http://127.0.0.1:5080 -DurationSeconds 60 -Concurrency 16 -AttackRatioPercent 20
# Select one configured Site when several Sites share the listener:
.\loadtest.ps1 -TargetUri http://127.0.0.1:5080 -HostHeader site-a.example -DurationSeconds 30
```

The runner passes `HostHeader` into each request, uses the worker's explicit target URL inside background jobs, and writes JSON/CSV evidence for the selected Site. Use only against a local synthetic upstream or an explicitly authorized environment.

The runner emits a JSON summary and a CSV request record. It reports throughput, status distribution, blocked requests, errors, and p50/p95/p99 latency. Keep the target local unless the owner has approved the test.
