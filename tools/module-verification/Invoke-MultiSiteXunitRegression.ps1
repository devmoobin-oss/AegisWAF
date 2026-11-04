[CmdletBinding()]
param([string]$EvidencePath = "")

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if ([string]::IsNullOrWhiteSpace($EvidencePath)) { $EvidencePath = Join-Path $root 'artifacts\module-verification\module54-xunit-multisite-regression-final.json' }
$evidenceFullPath = [System.IO.Path]::GetFullPath($EvidencePath)

$buildOutput = (& dotnet build (Join-Path $root 'tests\AegisWAF.Tests\AegisWAF.Tests.csproj') --no-restore -p:NuGetAudit=false -p:MSBuildEnableWorkloadResolver=false 2>&1 | Out-String)
$buildExit = $LASTEXITCODE
if ($buildExit -ne 0) { throw "xUnit test project build failed with exit code $buildExit`n$buildOutput" }

$runner = Join-Path $root 'tools\module-verification\xunit-inproc-runner\bin\Debug\net10.0\XunitInProcRunner.dll'
$runnerOutput = (& dotnet $runner UiContractTests SiteHealthTrendTests SiteHealthProbeTests HealthResponseWriterTests 2>&1 | Out-String)
$runnerExit = $LASTEXITCODE
$summary = [regex]::Match($runnerOutput, 'TARGET_SUMMARY passed=(\d+) failed=(\d+) expected=(\d+)')
$passed = if ($summary.Success) { [int]$summary.Groups[1].Value } else { 0 }
$failed = if ($summary.Success) { [int]$summary.Groups[2].Value } else { -1 }
$expected = if ($summary.Success) { [int]$summary.Groups[3].Value } else { 0 }
$success = $buildExit -eq 0 -and $runnerExit -eq 0 -and $summary.Success -and $failed -eq 0 -and $passed -eq $expected -and $expected -eq 27

$result = [ordered]@{
    module = 54
    success = $success
    progress = [ordered]@{ before = 0; after = if ($success) { 100 } else { 90 }; target = 100 }
    build = [ordered]@{ exitCode = $buildExit; project = 'tests/AegisWAF.Tests/AegisWAF.Tests.csproj' }
    runner = [ordered]@{ exitCode = $runnerExit; passed = $passed; failed = $failed; expected = $expected; classes = @('UiContractTests','SiteHealthTrendTests','SiteHealthProbeTests','HealthResponseWriterTests') }
    note = 'In-process xUnit runner is the deterministic gate; normal VSTest host connection remains a separate environment limitation.'
    recordedAt = [DateTimeOffset]::UtcNow.ToString('o')
}
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $evidenceFullPath) | Out-Null
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $evidenceFullPath -Encoding UTF8
if ($success) {
    Write-Output "MULTISITE_XUNIT_REGRESSION_PASS passed=$passed expected=$expected evidence=$evidenceFullPath"
    exit 0
}
Write-Error "MULTISITE_XUNIT_REGRESSION_FAIL passed=$passed failed=$failed expected=$expected evidence=$evidenceFullPath"
exit 1
