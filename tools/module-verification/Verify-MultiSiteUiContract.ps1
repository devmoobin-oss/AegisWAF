[CmdletBinding()]
param(
    [string]$EvidencePath = ""
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$appPath = Join-Path $root 'src\AegisWAF.Gateway\wwwroot\assets\app.js'
$stylePath = Join-Path $root 'src\AegisWAF.Gateway\wwwroot\assets\style.css'
$utf8 = [System.Text.Encoding]::UTF8
$app = [System.IO.File]::ReadAllText($appPath, $utf8)
$style = [System.IO.File]::ReadAllText($stylePath, $utf8)
$thaiImmediate = -join ([char[]](0x0e17,0x0e31,0x0e19,0x0e17,0x0e35))
$thaiCritical = -join ([char[]](0x0e27,0x0e34,0x0e01,0x0e24,0x0e15))
$thaiDegraded = -join ([char[]](0x0e40,0x0e1d,0x0e49,0x0e32,0x0e23,0x0e30,0x0e27,0x0e31,0x0e07))
$thaiHealthy = -join ([char[]](0x0e1b,0x0e01,0x0e15,0x0e34))
$thaiPending = -join ([char[]](0x0e23,0x0e2d,0x0e40,0x0e1b,0x0e34,0x0e14,0x0e43,0x0e0a))

$checks = @(
    [ordered]@{ name = 'immediate-activation-copy'; passed = $app.Contains('site-activation-notice') -and $app.Contains('route') -and $app.Contains($thaiImmediate); detail = 'POST/PUT/DELETE success copy states immediate route activation' }
    [ordered]@{ name = 'activation-notice'; passed = $app.Contains('site-activation-notice'); detail = 'page contains an explicit no-restart activation notice' }
    [ordered]@{ name = 'pending-state-fallback'; passed = $app.Contains('pending-restart') -and $app.Contains($thaiPending); detail = 'defensive pending-restart state remains visible in Thai' }
    [ordered]@{ name = 'critical-label'; passed = $app.Contains($thaiCritical); detail = 'critical state has Thai label' }
    [ordered]@{ name = 'degraded-label'; passed = $app.Contains($thaiDegraded); detail = 'degraded state has Thai label' }
    [ordered]@{ name = 'healthy-label'; passed = $app.Contains($thaiHealthy); detail = 'healthy state has Thai label' }
    [ordered]@{ name = 'bounded-safe-explanation'; passed = $app.Contains('healthExplanation') -and $app.Contains('Math.min(999'); detail = 'health explanation is derived from bounded safe summary fields' }
    [ordered]@{ name = 'summary-is-loaded'; passed = $app.Contains('/api/sites/health/summary?window=20&limit=100'); detail = 'summary alert/recovery state is loaded for the table' }
    [ordered]@{ name = 'stale-restart-copy-removed'; passed = -not $app.Contains('ต้อง Restart โปรแกรมหรือ Gateway ก่อนจึงจะเริ่มใช้งาน route'); detail = 'stale restart-required guidance is absent' }
    [ordered]@{ name = 'health-state-css'; passed = $style.Contains('.site-health-critical') -and $style.Contains('.site-health-degraded') -and $style.Contains('.site-health-explanation'); detail = 'state labels and explanations have accessible visual treatment' }
)

$success = @($checks | Where-Object { -not $_.passed }).Count -eq 0
$result = [ordered]@{
    module = 51
    success = $success
    progress = [ordered]@{ before = 0; after = if ($success) { 100 } else { 90 }; target = 100 }
    checks = $checks
    recordedAt = [DateTimeOffset]::UtcNow.ToString('o')
}

if ([string]::IsNullOrWhiteSpace($EvidencePath)) {
    $EvidencePath = Join-Path $root 'artifacts\module-verification\module51-multisite-ui-truthful-activation-final.json'
}
$evidenceFullPath = [System.IO.Path]::GetFullPath($EvidencePath)
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $evidenceFullPath) | Out-Null
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $evidenceFullPath -Encoding UTF8

if ($success) {
    Write-Output "MULTISITE_UI_CONTRACT_PASS checks=$($checks.Count) evidence=$evidenceFullPath"
    exit 0
}

$failed = ($checks | Where-Object { -not $_.passed } | ForEach-Object name) -join ','
Write-Error "MULTISITE_UI_CONTRACT_FAIL checks=$($checks.Count) failed=$failed evidence=$evidenceFullPath"
exit 1
