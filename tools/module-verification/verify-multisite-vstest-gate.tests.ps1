$ErrorActionPreference = 'Stop'

$scriptPath = Join-Path $PSScriptRoot 'Invoke-MultiSiteVstestRegression.ps1'
if (-not (Test-Path -LiteralPath $scriptPath)) {
    throw "Expected standard VSTest gate wrapper at $scriptPath"
}

$script = Get-Content -Raw -LiteralPath $scriptPath
if ($script -notmatch 'Invoke-Gate') { throw 'The VSTest gate must use the bounded Invoke-Gate helper.' }
if ($script -notmatch 'TimeoutSeconds') { throw 'The VSTest gate must expose a bounded timeout.' }
if ($script -notmatch 'FullyQualifiedName~UiContractTests') { throw 'The gate must include the UI contract class.' }
if ($script -notmatch 'FullyQualifiedName~SiteHealthTrendTests') { throw 'The gate must include the trend class.' }
if ($script -notmatch 'FullyQualifiedName~SiteHealthProbeTests') { throw 'The gate must include the probe class.' }
if ($script -notmatch 'FullyQualifiedName~HealthResponseWriterTests') { throw 'The gate must include the safe response writer class.' }
if ($script -notmatch 'Total tests') { throw 'The gate must parse the exact VSTest total.' }
if ($script -notmatch 'Passed') { throw 'The gate must parse the exact VSTest passed count.' }
if ($script -notmatch 'environmentBlocked') { throw 'The gate must distinguish a bounded environment block from a test failure.' }

Write-Output 'PASS: standard Multi-Site VSTest gate contract'
