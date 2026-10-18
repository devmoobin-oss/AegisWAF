[CmdletBinding()]
param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'artifacts'),
    [string]$LoadTestTargetUri = '',
    [switch]$SkipTests,
    [switch]$SkipDependencyScan,
    [switch]$SkipSecretScan,
    [switch]$SkipSast,
    [switch]$SkipSbom
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$RepoRoot = (Resolve-Path $RepoRoot).Path
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

$results = [System.Collections.Generic.List[object]]::new()

function Find-Tool([string]$name) {
    $command = Get-Command $name -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    if ($name -eq 'dotnet' -and (Test-Path 'C:\Program Files\dotnet\dotnet.exe')) { return 'C:\Program Files\dotnet\dotnet.exe' }
    return $null
}

function Add-Result([string]$name, [string]$status, [string]$tool, [string]$command, [int]$exitCode, [string]$output, [double]$durationSeconds) {
    $results.Add([pscustomobject]@{
        name = $name
        status = $status
        tool = $tool
        command = $command
        exitCode = $exitCode
        durationSeconds = [Math]::Round($durationSeconds, 3)
        output = if ($output.Length -gt 8000) { $output.Substring(0, 8000) } else { $output }
    })
}

function Get-GitCommit([string]$root) {
    try {
        $commit = (& git -C $root rev-parse HEAD 2>$null | Select-Object -First 1)
        if ($commit) { return $commit.ToString().Trim() }
    } catch { }
    return $null
}

function Get-ConfigurationFingerprint([string]$root) {
    $files = @(Get-ChildItem -LiteralPath $root -Recurse -File -Filter 'appsettings*.json' |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
        Sort-Object FullName)
    $rootPrefix = $root.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $entries = foreach ($file in $files) {
        $relative = $file.FullName.Substring($rootPrefix.Length).Replace('\', '/')
        $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        "$relative=$hash"
    }
    $canonical = ($entries -join "`n")
    $fingerprint = if ($canonical.Length -eq 0) { $null } else {
        $sha = [Security.Cryptography.SHA256]::Create()
        try {
            ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($canonical))).Replace('-', '')).ToLowerInvariant()
        } finally { $sha.Dispose() }
    }
    [pscustomobject]@{
        sha256 = $fingerprint
        files = @($entries | ForEach-Object { $_.Split('=', 2)[0] })
    }
}

function Run-External([string]$name, [string]$toolName, [string[]]$arguments, [string]$outputFile = '', [switch]$ClassifyInfrastructureFailure) {
    $tool = Find-Tool $toolName
    $commandText = (($toolName) + ' ' + ($arguments -join ' ')).Trim()
    if (-not $tool) {
        Add-Result $name 'unavailable' $toolName $commandText -1 "Tool '$toolName' is not installed or not on PATH." 0
        return
    }

    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    $captured = ''
    $exitCode = 0
    try {
        $captured = (& $tool @arguments 2>&1 | Out-String)
        $exitCode = $LASTEXITCODE
    } catch {
        $captured = $_ | Out-String
        $exitCode = 1
    }
    $watch.Stop()
    if ($outputFile) { Set-Content -LiteralPath $outputFile -Value $captured -Encoding UTF8 }
    $status = if ($exitCode -eq 0) { 'pass' }
        elseif ($ClassifyInfrastructureFailure -and $captured -match 'NU1301|SSL connection could not be established|No credentials are available in the security package') { 'unavailable' }
        else { 'fail' }
    Add-Result $name $status $toolName $commandText $exitCode $captured $watch.Elapsed.TotalSeconds
}

if ($SkipTests) {
    Add-Result 'dotnet tests' 'not_run' 'dotnet' 'skipped by parameter' -1 'Skipped by -SkipTests.' 0
} else {
    Run-External 'dotnet tests' 'dotnet' @('test', (Join-Path $RepoRoot 'tests\AegisWAF.Tests\AegisWAF.Tests.csproj'), '--no-restore', '--logger', 'console;verbosity=minimal')
}

if ($SkipDependencyScan) {
    Add-Result 'dependency vulnerabilities' 'not_run' 'dotnet' 'skipped by parameter' -1 'Skipped by -SkipDependencyScan.' 0
} else {
    Run-External 'dependency vulnerabilities' 'dotnet' @('list', (Join-Path $RepoRoot 'AegisWAF.slnx'), 'package', '--vulnerable', '--include-transitive') -ClassifyInfrastructureFailure
}

if ($SkipSecretScan) {
    Add-Result 'secret scan' 'not_run' 'gitleaks' 'skipped by parameter' -1 'Skipped by -SkipSecretScan.' 0
} else {
    Run-External 'secret scan' 'gitleaks' @('detect', '--source', $RepoRoot, '--no-banner', '--redact')
}

if ($SkipSast) {
    Add-Result 'SAST' 'not_run' 'semgrep' 'skipped by parameter' -1 'Skipped by -SkipSast.' 0
} else {
    Run-External 'SAST' 'semgrep' @('scan', '--config', 'auto', '--error', '--quiet', $RepoRoot)
}

if ($SkipSbom) {
    Add-Result 'SBOM' 'not_run' 'syft' 'skipped by parameter' -1 'Skipped by -SkipSbom.' 0
} else {
    $sbom = Join-Path $OutputDirectory 'aegiswaf-sbom.cyclonedx.json'
    Run-External 'SBOM' 'syft' @('dir:' + $RepoRoot, '-o', 'cyclonedx-json=' + $sbom)
}

if ([string]::IsNullOrWhiteSpace($LoadTestTargetUri)) {
    Add-Result 'security load test' 'not_run' 'PowerShell' 'target URI not supplied' -1 'Supply -LoadTestTargetUri for a live authorized target.' 0
} else {
    $loadtest = Join-Path $RepoRoot 'tools\security-loadtest\loadtest.ps1'
    $json = Join-Path $OutputDirectory 'security-loadtest.json'
    $csv = Join-Path $OutputDirectory 'security-loadtest.csv'
    Run-External 'security load test' 'powershell' @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $loadtest, '-TargetUri', $LoadTestTargetUri, '-OutputJson', $json, '-OutputCsv', $csv)
}

$gitCommit = Get-GitCommit $RepoRoot
$configFingerprint = Get-ConfigurationFingerprint $RepoRoot
$metadataReady = -not [string]::IsNullOrWhiteSpace($gitCommit) -and
    -not [string]::IsNullOrWhiteSpace($configFingerprint.sha256) -and
    @($configFingerprint.files).Count -gt 0
Add-Result 'assessment evidence metadata' $(if ($metadataReady) { 'pass' } else { 'unavailable' }) 'PowerShell' 'git commit + configuration fingerprint' $(if ($metadataReady) { 0 } else { 1 }) $(if ($metadataReady) { 'Commit and configuration fingerprint recorded.' } else { 'Could not compute commit and configuration fingerprint.' }) 0
$summary = [pscustomobject]@{
    generatedAt = [DateTimeOffset]::UtcNow
    repository = $RepoRoot
    gitCommit = $gitCommit
    configurationSha256 = $configFingerprint.sha256
    configurationFiles = $configFingerprint.files
    results = $results
    overall = if (@($results | Where-Object { $_.status -in @('fail', 'unavailable', 'not_run') }).Count -eq 0) { 'pass' } else { 'incomplete_or_failed' }
}
$summaryPath = Join-Path $OutputDirectory 'assessment-summary.json'
$summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $summaryPath -Encoding UTF8
$summary | ConvertTo-Json -Depth 6

if ($summary.overall -ne 'pass') { exit 2 }
