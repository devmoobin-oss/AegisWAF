$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$supervisor = Join-Path $PSScriptRoot 'Invoke-SiteRuntimeFaultInjection.ps1'
$evidence = Join-Path ([IO.Path]::GetTempPath()) ('aegiswaf-fault-injection-selftest-' + [Guid]::NewGuid().ToString('N') + '.json')
$old = $env:AEGISWAF_ENABLE_PG_FAULT_INJECTION
try {
    Remove-Item Env:AEGISWAF_ENABLE_PG_FAULT_INJECTION -ErrorAction SilentlyContinue
    & powershell -NoProfile -ExecutionPolicy Bypass -File $supervisor -ManagePostgres -PgCtlPath 'missing-pg-ctl.exe' -DataDirectory 'missing-data-directory' -EvidencePath $evidence
    $exitCode = $LASTEXITCODE
    if ($exitCode -eq 0) { throw 'Supervisor unexpectedly accepted a run without explicit opt-in' }
    if ($exitCode -ne 1) { throw "Supervisor returned unexpected exit code: $exitCode" }
}
finally {
    if ($null -eq $old) { Remove-Item Env:AEGISWAF_ENABLE_PG_FAULT_INJECTION -ErrorAction SilentlyContinue }
    else { $env:AEGISWAF_ENABLE_PG_FAULT_INJECTION = $old }
    Remove-Item -LiteralPath $evidence -Force -ErrorAction SilentlyContinue
}
Write-Output 'FAULT_INJECTION_SUPERVISOR_TEST_PASS opt_in_required=true lifecycle_flag_required=true'
