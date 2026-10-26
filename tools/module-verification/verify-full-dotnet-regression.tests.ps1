$ErrorActionPreference = 'Stop'

$scriptPath = Join-Path $PSScriptRoot 'Invoke-FullDotnetRegression.ps1'
if (-not (Test-Path -LiteralPath $scriptPath)) {
    throw "Expected full regression wrapper at $scriptPath"
}

$script = Get-Content -Raw -LiteralPath $scriptPath
if ($script -notmatch 'Invoke-Gate') { throw 'The full regression gate must use the bounded Invoke-Gate helper.' }
if ($script -notmatch 'TimeoutSeconds') { throw 'The full regression gate must expose a bounded timeout.' }
if ($script -notmatch 'dotnet test tests/AegisWAF.Tests/AegisWAF.Tests.csproj') { throw 'The gate must run the complete test project.' }
if ($script -notmatch 'Total:') { throw 'The gate must parse the exact total count.' }
if ($script -notmatch 'Passed:') { throw 'The gate must parse the exact passed count.' }
if ($script -notmatch 'Failed:') { throw 'The gate must parse the exact failed count.' }
if ($script -notmatch 'Skipped:') { throw 'The gate must parse the exact skipped count.' }
if ($script -notmatch 'environmentBlocked') { throw 'The gate must distinguish environment blocks from test failures.' }
if ($script -notmatch 'ExpectedTotal') { throw 'The gate must make the expected count explicit.' }

Write-Output 'PASS: full .NET regression gate contract'
