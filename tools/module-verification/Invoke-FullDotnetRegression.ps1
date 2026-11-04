[CmdletBinding()]
param(
    [string]$EvidencePath = '',
    [ValidateRange(1, 999)]
    [int]$ModuleNumber = 56,
    [ValidateRange(30, 900)]
    [int]$TimeoutSeconds = 300,
    [ValidateRange(1, 10000)]
    [int]$ExpectedTotal = 494
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if ([string]::IsNullOrWhiteSpace($EvidencePath)) {
    $EvidencePath = Join-Path $root ("artifacts\module-verification\module{0}-full-dotnet-regression-final.json" -f $ModuleNumber)
}
$evidenceFullPath = [System.IO.Path]::GetFullPath($EvidencePath)

. (Join-Path $PSScriptRoot 'verify-modules.ps1')

$command = @"
Set-Location -LiteralPath '$root'
dotnet test tests/AegisWAF.Tests/AegisWAF.Tests.csproj --no-build --no-restore --logger 'console;verbosity=minimal'
exit `$LASTEXITCODE
"@
$gate = Invoke-Gate -Command $command -TimeoutSeconds $TimeoutSeconds
$combined = ($gate.stdout + "`n" + $gate.stderr).Trim()

$totalMatch = [regex]::Match($combined, '(?im)\bTotal:\s*(\d+)')
$passedMatch = [regex]::Match($combined, '(?im)\bPassed:\s*(\d+)')
$failedMatch = [regex]::Match($combined, '(?im)\bFailed:\s*(\d+)')
$skippedMatch = [regex]::Match($combined, '(?im)\bSkipped:\s*(\d+)')
$total = if ($totalMatch.Success) { [int]$totalMatch.Groups[1].Value } else { 0 }
$passed = if ($passedMatch.Success) { [int]$passedMatch.Groups[1].Value } else { 0 }
$failed = if ($failedMatch.Success) { [int]$failedMatch.Groups[1].Value } else { -1 }
$skipped = if ($skippedMatch.Success) { [int]$skippedMatch.Groups[1].Value } else { 0 }
$environmentBlocked = $gate.status -eq 'timeout' -or $combined -match '(?i)(testhost process failed to connect|waiting for connection|connection.*testhost|accessdenied|socketerror)'
$success = $gate.status -eq 'passed' -and -not $environmentBlocked -and $total -eq $ExpectedTotal -and $passed -eq $ExpectedTotal -and $failed -eq 0 -and $skipped -eq 0

$result = [ordered]@{
    module = $ModuleNumber
    success = $success
    progress = [ordered]@{ before = 0; after = if ($success) { 100 } else { 90 }; target = 100 }
    gate = [ordered]@{
        status = $gate.status
        exitCode = $gate.exitCode
        durationMs = $gate.durationMs
        timeoutSeconds = $TimeoutSeconds
        command = 'dotnet test tests/AegisWAF.Tests/AegisWAF.Tests.csproj --no-build --no-restore --logger console;verbosity=minimal'
    }
    tests = [ordered]@{ total = $total; passed = $passed; failed = $failed; skipped = $skipped; expected = $ExpectedTotal }
    environmentBlocked = $environmentBlocked
    requiresLoopback = $true
    stdoutTail = if ($gate.stdout.Length -gt 6000) { $gate.stdout.Substring($gate.stdout.Length - 6000) } else { $gate.stdout }
    stderrTail = if ($gate.stderr.Length -gt 6000) { $gate.stderr.Substring($gate.stderr.Length - 6000) } else { $gate.stderr }
    note = 'Full regression is accepted only with exact expected counts. A bounded timeout or testhost/loopback limitation is reported separately from a failing test.'
    recordedAt = [DateTimeOffset]::UtcNow.ToString('o')
}
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $evidenceFullPath) | Out-Null
$result | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $evidenceFullPath -Encoding UTF8

if ($success) {
    Write-Output "FULL_DOTNET_REGRESSION_PASS passed=$passed expected=$ExpectedTotal evidence=$evidenceFullPath"
    exit 0
}
if ($environmentBlocked) {
    Write-Error "FULL_DOTNET_REGRESSION_ENVIRONMENT_BLOCKED status=$($gate.status) passed=$passed total=$total evidence=$evidenceFullPath"
    exit 2
}
Write-Error "FULL_DOTNET_REGRESSION_FAIL status=$($gate.status) passed=$passed failed=$failed total=$total skipped=$skipped evidence=$evidenceFullPath"
exit 1
