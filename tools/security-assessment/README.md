# AegisWAF security assessment runner

`run-assessment.ps1` provides a repeatable, fail-closed evidence wrapper for local or authorized environments.

```powershell
.\tools\security-assessment\run-assessment.ps1 -LoadTestTargetUri http://127.0.0.1:5080
```

It records `assessment-summary.json`, command output, and (when available) SBOM/load-test artifacts under `tools/security-assessment/artifacts/`. Missing tools and omitted live load-test targets are reported as `unavailable`/`not_run` and make the overall result `incomplete_or_failed`; they are never silently treated as passing.

The runner can be used for an offline regression-only run, but skipped checks remain visible:

```powershell
.\tools\security-assessment\run-assessment.ps1 -SkipDependencyScan -SkipSecretScan -SkipSast -SkipSbom
```

Run external tools only against a synthetic or explicitly authorized target. This runner is evidence collection, not a penetration-test or certification tool.
