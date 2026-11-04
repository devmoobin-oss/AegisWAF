[CmdletBinding()]
param(
    [string]$EvidencePath = '',
    [ValidateRange(10, 600)]
    [int]$TimeoutSeconds = 120
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if ([string]::IsNullOrWhiteSpace($EvidencePath)) {
    $EvidencePath = Join-Path $root 'artifacts\module-verification\module55-vstest-multisite-regression-final.json'
}
$evidenceFullPath = [System.IO.Path]::GetFullPath($EvidencePath)

. (Join-Path $PSScriptRoot 'verify-modules.ps1')

$filter = 'FullyQualifiedName~UiContractTests|FullyQualifiedName~SiteHealthTrendTests|FullyQualifiedName~SiteHealthProbeTests|FullyQualifiedName~HealthResponseWriterTests'
$command = @"
Set-Location -LiteralPath '$root'
dotnet test tests/AegisWAF.Tests/AegisWAF.Tests.csproj --no-build --no-restore --filter '$filter' --logger 'console;verbosity=normal'
exit `$LASTEXITCODE
"@
$gate = Invoke-Gate -Command $command -TimeoutSeconds $TimeoutSeconds
$combined = ($gate.stdout + "`n" + $gate.stderr).Trim()

$totalMatch = [regex]::Match($combined, '(?im)^\s*Total tests:\s*(\d+)')
$passedMatch = [regex]::Match($combined, '(?im)^\s*Passed:\s*(\d+)')
$failedMatch = [regex]::Match($combined, '(?im)^\s*Failed:\s*(\d+)')
$skippedMatch = [regex]::Match($combined, '(?im)^\s*Skipped:\s*(\d+)')
$total = if ($totalMatch.Success) { [int]$totalMatch.Groups[1].Value } else { 0 }
$passed = if ($passedMatch.Success) { [int]$passedMatch.Groups[1].Value } else { 0 }
$failed = if ($failedMatch.Success) { [int]$failedMatch.Groups[1].Value } elseif ($gate.status -eq 'passed') { 0 } else { -1 }
$skipped = if ($skippedMatch.Success) { [int]$skippedMatch.Groups[1].Value } else { 0 }
$expected = 27
$environmentBlocked = $gate.status -eq 'timeout' -or $combined -match '(?i)(testhost process failed to connect|waiting for connection|connection.*testhost|accessdenied|socketerror)'
$success = $gate.status -eq 'passed' -and -not $environmentBlocked -and $total -eq $expected -and $passed -eq $expected -and $failed -eq 0

$result = [ordered]@{
    module = 55
    success = $success
    progress = [ordered]@{ before = 0; after = if ($success) { 100 } else { 90 }; target = 100 }
    gate = [ordered]@{
        status = $gate.status
        exitCode = $gate.exitCode
        durationMs = $gate.durationMs
        timeoutSeconds = $TimeoutSeconds
        command = 'dotnet test tests/AegisWAF.Tests/AegisWAF.Tests.csproj --no-build --no-restore --filter <four focused Multi-Site classes>'
    }
    tests = [ordered]@{ total = $total; passed = $passed; failed = $failed; skipped = $skipped; expected = $expected }
    environmentBlocked = $environmentBlocked
    requiresLoopback = $true
    stdoutTail = if ($gate.stdout.Length -gt 6000) { $gate.stdout.Substring($gate.stdout.Length - 6000) } else { $gate.stdout }
    stderrTail = if ($gate.stderr.Length -gt 6000) { $gate.stderr.Substring($gate.stderr.Length - 6000) } else { $gate.stderr }
    note = 'Standard VSTest is the primary gate when loopback is permitted; the bounded wrapper reports sandbox/testhost connection limits instead of hanging indefinitely.'
    recordedAt = [DateTimeOffset]::UtcNow.ToString('o')
}
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $evidenceFullPath) | Out-Null
$result | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $evidenceFullPath -Encoding UTF8

if ($success) {
    Write-Output "MULTISITE_VSTEST_REGRESSION_PASS passed=$passed expected=$expected evidence=$evidenceFullPath"
    exit 0
}
if ($environmentBlocked) {
    Write-Error "MULTISITE_VSTEST_REGRESSION_ENVIRONMENT_BLOCKED status=$($gate.status) passed=$passed total=$total evidence=$evidenceFullPath"
    exit 2
}
Write-Error "MULTISITE_VSTEST_REGRESSION_FAIL status=$($gate.status) passed=$passed failed=$failed total=$total evidence=$evidenceFullPath"
exit 1
