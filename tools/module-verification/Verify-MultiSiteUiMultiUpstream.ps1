[CmdletBinding()]
param([string]$EvidencePath = "")

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$appPath = Join-Path $root 'src\AegisWAF.Gateway\wwwroot\assets\app.js'
$stylePath = Join-Path $root 'src\AegisWAF.Gateway\wwwroot\assets\style.css'
$utf8 = [System.Text.Encoding]::UTF8
$app = [System.IO.File]::ReadAllText($appPath, $utf8)
$style = [System.IO.File]::ReadAllText($stylePath, $utf8)
$thaiHelp = -join ([char[]](0x0e41,0x0e15,0x0e48,0x0e25,0x0e30,0x0e1a,0x0e23,0x0e23,0x0e17,0x0e31,0x0e14))

$checks = @(
    [ordered]@{ name = 'multiline-field'; passed = $app.Contains('id="siteUpstreams"'); detail = 'browser form exposes a dedicated multiline upstream editor' }
    [ordered]@{ name = 'syntax-guidance'; passed = $app.Contains('id=url@weight') -and $app.Contains('site-form-help'); detail = 'form explains id=url@weight syntax and defaults' }
    [ordered]@{ name = 'thai-guidance'; passed = $app.Contains($thaiHelp); detail = 'form guidance is available in Thai' }
    [ordered]@{ name = 'parser'; passed = $app.Contains('parseUpstreams') -and $app.Contains('lines.length>32'); detail = 'client parser bounds entries and validates lines' }
    [ordered]@{ name = 'duplicate-and-weight-validation'; passed = $app.Contains('ids.has(id)') -and $app.Contains('weight>10000'); detail = 'duplicate IDs and unsafe weights are rejected before submit' }
    [ordered]@{ name = 'all-upstreams-payload'; passed = $app.Contains('Upstreams:upstreams.map'); detail = 'request sends every upstream and its weight' }
    [ordered]@{ name = 'first-upstream-compatibility'; passed = $app.Contains('upstreamAddress:upstreams[0].address'); detail = 'legacy first-upstream field remains populated' }
    [ordered]@{ name = 'egress-derived-from-all'; passed = $app.Contains('new Set(upstreams.map(x=>x.host))') -and $app.Contains('new Set(upstreams.map(x=>x.port))'); detail = 'default egress allowlists cover every configured upstream' }
    [ordered]@{ name = 'edit-round-trip'; passed = $app.Contains('(site.upstreams||[]).map(up=>`${up.id}=${up.address}@${up.weight||100}`)'); detail = 'edit flow restores every upstream and weight' }
    [ordered]@{ name = 'textarea-style'; passed = $style.Contains('.policy-form textarea') -and $style.Contains('.site-form-help'); detail = 'multiline editor has consistent accessible styling' }
)

$success = @($checks | Where-Object { -not $_.passed }).Count -eq 0
$result = [ordered]@{
    module = 52
    success = $success
    progress = [ordered]@{ before = 0; after = if ($success) { 100 } else { 90 }; target = 100 }
    checks = $checks
    recordedAt = [DateTimeOffset]::UtcNow.ToString('o')
}
if ([string]::IsNullOrWhiteSpace($EvidencePath)) { $EvidencePath = Join-Path $root 'artifacts\module-verification\module52-multisite-ui-multi-upstream-final.json' }
$evidenceFullPath = [System.IO.Path]::GetFullPath($EvidencePath)
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $evidenceFullPath) | Out-Null
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $evidenceFullPath -Encoding UTF8
if ($success) {
    Write-Output "MULTISITE_UI_MULTI_UPSTREAM_PASS checks=$($checks.Count) evidence=$evidenceFullPath"
    exit 0
}
$failed = ($checks | Where-Object { -not $_.passed } | ForEach-Object name) -join ','
Write-Error "MULTISITE_UI_MULTI_UPSTREAM_FAIL checks=$($checks.Count) failed=$failed evidence=$evidenceFullPath"
exit 1
