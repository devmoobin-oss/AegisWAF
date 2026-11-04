[CmdletBinding()]
param([string]$EvidencePath = "")

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$appPath = Join-Path $root 'src\AegisWAF.Gateway\wwwroot\assets\app.js'
$stylePath = Join-Path $root 'src\AegisWAF.Gateway\wwwroot\assets\style.css'
$utf8 = [System.Text.Encoding]::UTF8
$app = [System.IO.File]::ReadAllText($appPath, $utf8)
$style = [System.IO.File]::ReadAllText($stylePath, $utf8)

$checks = @(
    [ordered]@{ name = 'health-fields'; passed = $app.Contains('siteHealthPath') -and $app.Contains('siteHealthInterval') -and $app.Contains('siteHealthTimeout') -and $app.Contains('siteHealthPassive'); detail = 'Health path and timing fields are present' }
    [ordered]@{ name = 'rate-fields'; passed = $app.Contains('siteRateRequests') -and $app.Contains('siteRateWindow') -and $app.Contains('siteRateGlobal'); detail = 'per-Site rate limit fields are present' }
    [ordered]@{ name = 'health-payload'; passed = $app.Contains('Health:{Path:healthPath') -and $app.Contains('PassiveReactivationSeconds:healthPassive'); detail = 'Health policy is sent to the API' }
    [ordered]@{ name = 'rate-payload'; passed = $app.Contains('RateLimit:{Requests:rateRequests') -and $app.Contains('GlobalRequests:rateGlobal'); detail = 'RateLimit policy is sent to the API' }
    [ordered]@{ name = 'path-validation'; passed = $app.Contains('!/^\/[^?#\0]{0,127}$/'); detail = 'health path is relative and bounded' }
    [ordered]@{ name = 'health-bounds'; passed = $app.Contains('healthInterval<5') -and $app.Contains('healthInterval>300') -and $app.Contains('healthTimeout>=healthInterval') -and $app.Contains('healthPassive>3600'); detail = 'health timing bounds match server policy' }
    [ordered]@{ name = 'rate-bounds'; passed = $app.Contains('rateRequests>1000000') -and $app.Contains('rateWindow>3600') -and $app.Contains('rateGlobal>10000000'); detail = 'rate bounds match server policy' }
    [ordered]@{ name = 'edit-health-round-trip'; passed = $app.Contains("site.healthPath||'/'") -and $app.Contains("site.healthPassiveReactivationSeconds||30"); detail = 'edit flow restores saved Health policy' }
    [ordered]@{ name = 'edit-rate-round-trip'; passed = $app.Contains("site.rateRequests||300") -and $app.Contains("site.rateGlobalRequests||10000"); detail = 'edit flow restores saved RateLimit policy' }
    [ordered]@{ name = 'policy-guidance-style'; passed = $app.Contains('site-form-help') -and $style.Contains('.site-form-help'); detail = 'guidance explains Health and Rate Limit fields' }
)

$success = @($checks | Where-Object { -not $_.passed }).Count -eq 0
$result = [ordered]@{
    module = 53
    success = $success
    progress = [ordered]@{ before = 0; after = if ($success) { 100 } else { 90 }; target = 100 }
    checks = $checks
    recordedAt = [DateTimeOffset]::UtcNow.ToString('o')
}
if ([string]::IsNullOrWhiteSpace($EvidencePath)) { $EvidencePath = Join-Path $root 'artifacts\module-verification\module53-multisite-ui-policy-fields-final.json' }
$evidenceFullPath = [System.IO.Path]::GetFullPath($EvidencePath)
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $evidenceFullPath) | Out-Null
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $evidenceFullPath -Encoding UTF8
if ($success) {
    Write-Output "MULTISITE_UI_POLICY_FIELDS_PASS checks=$($checks.Count) evidence=$evidenceFullPath"
    exit 0
}
$failed = ($checks | Where-Object { -not $_.passed } | ForEach-Object name) -join ','
Write-Error "MULTISITE_UI_POLICY_FIELDS_FAIL checks=$($checks.Count) failed=$failed evidence=$evidenceFullPath"
exit 1
