[CmdletBinding()]
param(
    [string]$Module,
    [switch]$All,
    [switch]$SkipTests,
    [switch]$UpdateHandoff,
    [string]$OutputDirectory = 'artifacts/module-verification'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-ModuleManifest([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { throw "Module manifest not found: $Path" }
    return (Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json)
}

function Resolve-Module($Manifest, [string]$Id) {
    $module = @($Manifest.modules | Where-Object { $_.id -eq $Id }) | Select-Object -First 1
    if ($null -eq $module) { throw "Unknown module: $Id" }
    return $module
}

function Invoke-Gate([string]$Command, [int]$TimeoutSeconds = 120) {
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = 'powershell.exe'
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $encodedCommand = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($Command))
    $psi.Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand $encodedCommand"
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $psi
    [void]$process.Start()
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $timedOut = $false
    try {
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            $timedOut = $true
            try { $process.Kill($true) } catch { }
            [void]$process.WaitForExit(5000)
        }
    } finally {
        $watch.Stop()
    }
    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    $status = if ($timedOut) { 'timeout' } elseif ($process.ExitCode -eq 0) { 'passed' } else { 'failed' }
    [pscustomobject]@{
        status = $status
        exitCode = if ($timedOut) { $null } else { $process.ExitCode }
        durationMs = [Math]::Round($watch.Elapsed.TotalMilliseconds, 0)
        stdout = $stdout.Trim()
        stderr = $stderr.Trim()
    }
}

function Invoke-ModuleVerification($Manifest, $SelectedModule, [switch]$SkipTests) {
    $results = [System.Collections.Generic.List[object]]::new()
    foreach ($gate in @($SelectedModule.gates)) {
        if ($SkipTests -and $gate.kind -eq 'test') {
            $results.Add([pscustomobject]@{ id = $gate.id; kind = $gate.kind; weight = [int]$gate.weight; command = $gate.command; status = 'skipped'; exitCode = $null; durationMs = 0; stdout = ''; stderr = 'Skipped by -SkipTests' })
            continue
        }
        $outcome = Invoke-Gate -Command $gate.command -TimeoutSeconds ([int]$gate.timeoutSeconds)
        $results.Add([pscustomobject]@{ id = $gate.id; kind = $gate.kind; weight = [int]$gate.weight; command = $gate.command; status = $outcome.status; exitCode = $outcome.exitCode; durationMs = $outcome.durationMs; stdout = $outcome.stdout; stderr = $outcome.stderr })
    }
    $passedWeight = [int](($results | Where-Object { $_.status -eq 'passed' } | Measure-Object -Property weight -Sum).Sum)
    if ($null -eq $passedWeight) { $passedWeight = 0 }
    [pscustomobject]@{
        id = $SelectedModule.id
        name = $SelectedModule.name
        percentage = $passedWeight
        target = 99
        gates = @($results)
        passed = @($results | Where-Object { $_.status -eq 'passed' }).Count
        unverified = @($results | Where-Object { $_.status -in @('timeout', 'skipped') }).Count
        failed = @($results | Where-Object { $_.status -eq 'failed' }).Count
    }
}

function Invoke-VerificationRun {
    param(
        [string]$ManifestPath,
        [string]$SelectedId,
        [switch]$RunAll,
        [switch]$SkipTestGates,
        [string]$ReportDirectory
    )
    $manifest = Get-ModuleManifest -Path $ManifestPath
    if (-not $RunAll -and [string]::IsNullOrWhiteSpace($SelectedId)) { throw 'Provide -Module <id> or -All.' }
    $modules = if ($RunAll) { @($manifest.modules) } else { @(Resolve-Module -Manifest $manifest -Id $SelectedId) }
    $reports = foreach ($item in $modules) { Invoke-ModuleVerification -Manifest $manifest -SelectedModule $item -SkipTests:$SkipTestGates }
    $root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
    $directory = Join-Path $root $ReportDirectory
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    $stamp = [DateTimeOffset]::Now.ToString('yyyyMMdd-HHmmssfff')
    $path = Join-Path $directory "$stamp.json"
    $report = [pscustomobject]@{ generatedAt = [DateTimeOffset]::Now; repository = $root; modules = @($reports) }
    $report | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $path -Encoding UTF8
    [pscustomobject]@{ path = $path; report = $report }
}

function ConvertTo-ProgressNumber([string]$Value) {
    $match = [regex]::Match($Value, '(?<number>\d+(?:\.\d+)?)%')
    if ($match.Success) { return [double]$match.Groups['number'].Value }
    return 0
}

function Update-Handoff {
    param(
        [string]$HandoffPath,
        $Module,
        $Verification,
        [string]$EvidencePath
    )
    if (-not (Test-Path -LiteralPath $HandoffPath)) { throw "Handoff file not found: $HandoffPath" }
    $utf8 = [System.Text.UTF8Encoding]::new($false)
    $lines = [System.Collections.Generic.List[string]]::new()
    foreach ($line in @([System.IO.File]::ReadAllLines($HandoffPath, $utf8))) { $lines.Add([string]$line) }
    $pattern = '^\|\s*\d+\.\s*' + [regex]::Escape([string]$Module.name) + '\s*\|'
    $rowIndex = -1
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match $pattern) { $rowIndex = $i; break }
    }
    if ($rowIndex -lt 0) { throw "Module row not found in handoff: $($Module.name)" }

    $columns = $lines[$rowIndex].Split('|') | ForEach-Object { $_.Trim() }
    $before = ConvertTo-ProgressNumber $columns[3]
    $after = [double]$Verification.percentage
    if ([int]$Verification.failed -gt 0 -or [int]$Verification.unverified -gt 0) {
        $after = [Math]::Min(98, $after)
    }
    $beforeText = if ($before -eq [Math]::Truncate($before)) { '{0}%' -f [int]$before } else { '{0:0.##}%' -f $before }
    $afterText = if ($after -eq [Math]::Truncate($after)) { '{0}%' -f [int]$after } else { '{0:0.##}%' -f $after }
    $status = if ($after -gt 98 -and [int]$Verification.failed -eq 0 -and [int]$Verification.unverified -eq 0) { 'passed >98%' } else { 'in progress' }
    $summary = "report=$EvidencePath; passed=$($Verification.passed); failed=$($Verification.failed); unverified=$($Verification.unverified)"
    $ordinal = ([regex]::Match($lines[$rowIndex], '^\|\s*(\d+)\.')).Groups[1].Value
    $lines[$rowIndex] = "| $ordinal. $($Module.name) | $beforeText | $afterText | >98% | $summary | $status |"

    $historyIndex = -1
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -eq '## Verification history') { $historyIndex = $i; break }
    }
    if ($historyIndex -lt 0) {
        $lines.Add('')
        $lines.Add('## Verification history')
    }
    $lines.Add('')
    $lines.Add("### $([DateTimeOffset]::Now.ToString('yyyy-MM-dd HH:mm:ss zzz')) -- $($Module.name)")
    $lines.Add("- Before: $beforeText; After: $afterText; Target: >98%")
    $lines.Add("- Evidence: $EvidencePath")
    $lines.Add("- Gates: passed=$($Verification.passed), failed=$($Verification.failed), unverified=$($Verification.unverified)")
    [System.IO.File]::WriteAllLines($HandoffPath, $lines, $utf8)
    [pscustomobject]@{ module = $Module.id; before = $before; after = $after; status = $status; evidence = $EvidencePath }
}

if ($MyInvocation.InvocationName -ne '.') {
    $manifestPath = Join-Path $PSScriptRoot 'modules.json'
    $run = Invoke-VerificationRun -ManifestPath $manifestPath -SelectedId $Module -RunAll:$All -SkipTestGates:$SkipTests -ReportDirectory $OutputDirectory
    if ($UpdateHandoff) {
        $handoffPath = Join-Path (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path 'handoff.md'
        foreach ($item in @($run.report.modules)) {
            $definition = Resolve-Module -Manifest (Get-ModuleManifest -Path $manifestPath) -Id $item.id
            Update-Handoff -HandoffPath $handoffPath -Module $definition -Verification $item -EvidencePath $run.path | Out-Null
        }
    }
    $run.report | ConvertTo-Json -Depth 20
}
