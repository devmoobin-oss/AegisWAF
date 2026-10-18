# AegisWAF security assessment runner

`run-assessment.ps1` provides a repeatable, fail-closed evidence wrapper for local or authorized environments.

```powershell
.\tools\security-assessment\run-assessment.ps1 -LoadTestTargetUri http://127.0.0.1:5080
```

It records `assessment-summary.json`, command output, and (when available) SBOM/load-test artifacts under `tools/security-assessment/artifacts/`. The summary also records the current Git commit and a SHA-256 fingerprint of source `appsettings*.json` files (excluding generated `bin/` and `obj/` output). Missing tools and omitted live load-test targets are reported as `unavailable`/`not_run` and make the overall result `incomplete_or_failed`; they are never silently treated as passing.

The summary includes an `assessment evidence metadata` result; missing commit or configuration fingerprints are themselves fail-closed and cannot be mistaken for a complete assessment. Dependency-feed failures such as NuGet `NU1301`/TLS credential errors, and test-build file locks such as MSBuild `MSB3027`/`MSB3021` when a live target uses the same output, are recorded as `unavailable`, not as confirmed vulnerabilities or test failures.

The runner can be used for an offline regression-only run, but skipped checks remain visible:

```powershell
.\tools\security-assessment\run-assessment.ps1 -SkipDependencyScan -SkipSecretScan -SkipSast -SkipSbom
```

Run external tools only against a synthetic or explicitly authorized target. This runner is evidence collection, not a penetration-test or certification tool.
