[CmdletBinding()]
param(
    [switch]$ManagePostgres,
    [string]$PgCtlPath = 'C:\Program Files\PostgreSQL\18\bin\pg_ctl.exe',
    [string]$DataDirectory = 'C:\Program Files\PostgreSQL\18\data',
    [int]$ReadyTimeoutSeconds = 45,
    [int]$OutageTimeoutSeconds = 30,
    [int]$RecoveryTimeoutSeconds = 60,
    [string]$EvidencePath = ''
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$runnerProject = Join-Path $PSScriptRoot 'SiteRuntimeFaultInjectionRunner.csproj'
$runnerExe = Join-Path $PSScriptRoot 'bin\Debug\net10.0\SiteRuntimeFaultInjectionRunner.exe'
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('aegiswaf-fault-injection-' + [Guid]::NewGuid().ToString('N'))
$statusFile = Join-Path $tempRoot 'runner-status.json'
$supervisorStateFile = Join-Path $tempRoot 'supervisor-state.json'
$stopSentinel = Join-Path $tempRoot 'stop.sentinel'
$runnerStdout = Join-Path $tempRoot 'runner.stdout.log'
$runnerStderr = Join-Path $tempRoot 'runner.stderr.log'
$runner = $null
$initialListening = $false
$ownedPostgres = $false
$success = $false
$failureType = $null
$lastReadyProbe = $null
$phases = [System.Collections.Generic.List[object]]::new()

function Set-SupervisorState([string]$Name, [object]$Details = $null) {
    try {
        [pscustomobject]@{ phase = $Name; details = $Details; recordedAt = [DateTimeOffset]::UtcNow } |
            ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $supervisorStateFile -Encoding UTF8
    } catch { }
}

function Add-Phase([string]$Name, [string]$Result, [object]$Details = $null) {
    $phases.Add([pscustomobject]@{ name = $Name; result = $Result; details = $Details; recordedAt = [DateTimeOffset]::UtcNow })
    Set-SupervisorState "${Name}:$Result" $Details
}

function Test-PostgresListening {
    $pgIsReady = Join-Path (Split-Path -Parent $PgCtlPath) 'pg_isready.exe'
    if (Test-Path -LiteralPath $pgIsReady) {
        $previous = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try {
            & $pgIsReady -h 127.0.0.1 -p 5432 *> $null
            return [int]$LASTEXITCODE -eq 0
        } finally { $ErrorActionPreference = $previous }
    }
    try {
        if (Get-NetTCPConnection -LocalPort 5432 -State Listen -ErrorAction Stop) { return $true }
    } catch { }
    return [bool](netstat -ano 2>$null | Select-String ':5432\s+.*LISTENING')
}

function Invoke-PgCtl([string[]]$Arguments) {
    $outPath = Join-Path $tempRoot ('pgctl-' + [Guid]::NewGuid().ToString('N') + '.out.log')
    $errPath = Join-Path $tempRoot ('pgctl-' + [Guid]::NewGuid().ToString('N') + '.err.log')
    $argumentString = ($Arguments | ForEach-Object { Quote-Arg ([string]$_) }) -join ' '
    $process = Start-Process -FilePath $PgCtlPath -ArgumentList $argumentString -PassThru -RedirectStandardOutput $outPath -RedirectStandardError $errPath
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 100
        $process.Refresh()
    } while (-not $process.HasExited -and [DateTimeOffset]::UtcNow -lt $deadline)
    if (-not $process.HasExited) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        $exitCode = 124
    } else { $exitCode = [int]$process.ExitCode }
    $lines = @()
    if (Test-Path -LiteralPath $outPath) { $lines += Get-Content -LiteralPath $outPath -ErrorAction SilentlyContinue }
    if (Test-Path -LiteralPath $errPath) { $lines += Get-Content -LiteralPath $errPath -ErrorAction SilentlyContinue }
    [pscustomobject]@{
        exitCode = $exitCode
        outputLines = $lines.Count
        output = ($lines | Select-Object -Last 3) -join ' | '
    }
}

function Remove-StalePostmasterLock {
    $status = Invoke-PgCtl @('-D', $DataDirectory, 'status')
    if ($status.exitCode -eq 0) { return $false }
    $lockPath = Join-Path $DataDirectory 'postmaster.pid'
    if (-not (Test-Path -LiteralPath $lockPath)) { return $false }
    $pidText = (Get-Content -LiteralPath $lockPath -ErrorAction SilentlyContinue | Select-Object -First 1)
    $pid = 0
    if ([int]::TryParse([string]$pidText, [ref]$pid) -and (Get-Process -Id $pid -ErrorAction SilentlyContinue)) {
        throw [InvalidOperationException]::new('PostgreSQL postmaster.pid belongs to a running process')
    }
    Remove-Item -LiteralPath $lockPath -Force
    return $true
}

function Get-ReadyProbe([int]$Port) {
    $uri = "http://127.0.0.1:$Port/ready"
    try {
        $curlOutput = @(& curl.exe --noproxy '*' --max-time 3 --silent --show-error --write-out "`n__AEGIS_HTTP_STATUS__%{http_code}" $uri 2>$null)
        $raw = $curlOutput -join [Environment]::NewLine
        $marker = "__AEGIS_HTTP_STATUS__"
        $markerIndex = $raw.LastIndexOf($marker, [StringComparison]::Ordinal)
        if ($markerIndex -lt 0) { throw [InvalidOperationException]::new('curl did not return an HTTP status') }
        $body = $raw.Substring(0, $markerIndex).TrimEnd("`r", "`n")
        $statusText = $raw.Substring($markerIndex + $marker.Length).Trim()
        $statusCode = 0
        [void][int]::TryParse($statusText, [ref]$statusCode)
        if ($statusCode -eq 0 -and [string]::IsNullOrWhiteSpace($body)) { throw [InvalidOperationException]::new('curl could not connect to /ready') }
        $hash = [Security.Cryptography.SHA256]::Create()
        try { $bodySha256 = [BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($body))).Replace('-', '') }
        finally { $hash.Dispose() }
        $payload = $body | ConvertFrom-Json
        $listenerStatus = [string]$payload.entries.'site-runtime-listener'.status
        $postgresStatus = [string]$payload.entries.postgres.status
        return [pscustomobject]@{
            statusCode = $statusCode
            bodyLength = $body.Length
            bodySha256 = $bodySha256
            overallStatus = [string]$payload.status
            hasListener = -not [string]::IsNullOrWhiteSpace($listenerStatus)
            listenerStatus = $listenerStatus
            postgresStatus = $postgresStatus
            listenerHealthy = $listenerStatus -eq 'Healthy'
            listenerDegraded = $listenerStatus -in @('Degraded', 'Unhealthy')
            postgresHealthy = $postgresStatus -eq 'Healthy'
            postgresDegraded = $postgresStatus -in @('Degraded', 'Unhealthy')
        }
    } catch {
        return [pscustomobject]@{
            statusCode = 0
            bodyLength = 0
            bodySha256 = ''
            hasListener = $false
            overallStatus = ''
            listenerStatus = ''
            postgresStatus = ''
            listenerHealthy = $false
            listenerDegraded = $false
            postgresHealthy = $false
            postgresDegraded = $false
            errorType = $_.Exception.GetType().Name
            errorMessage = $_.Exception.Message
        }
    }
}

function Get-RunnerStatus {
    if (-not (Test-Path -LiteralPath $statusFile)) { return $null }
    try { return Get-Content -Raw -LiteralPath $statusFile | ConvertFrom-Json } catch { return $null }
}

function Get-SafeDiagnostic([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    $text = (Get-Content -Raw -LiteralPath $Path -ErrorAction SilentlyContinue)
    if ([string]::IsNullOrWhiteSpace($text)) { return $null }
    $text = $text -replace '(?i)(password\s*[=:])[^;\r\n ]+', '$1[redacted]'
    if ($text.Length -gt 2000) { $text = $text.Substring($text.Length - 2000) }
    return $text.Trim()
}

function Wait-RunnerReady([int]$TimeoutSeconds) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $status = Get-RunnerStatus
        if ($status -and $status.phase -eq 'failed') { throw [InvalidOperationException]::new('Runner failed before becoming ready') }
        if ($status -and $status.phase -eq 'runner-ready') { return $status }
        Start-Sleep -Milliseconds 250
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw [TimeoutException]::new('Runner did not become ready within the bounded timeout')
}

function Wait-ReadyState([int]$Port, [bool]$Healthy, [int]$TimeoutSeconds) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    $last = $null
    do {
        $last = Get-ReadyProbe $Port
        $script:lastReadyProbe = $last
        $isExpected = if ($Healthy) {
            $last.statusCode -in @(200, 503) -and $last.hasListener -and $last.listenerHealthy -and $last.postgresHealthy
        } else {
            $last.statusCode -ne 0 -and $last.hasListener -and ($last.listenerDegraded -or $last.postgresDegraded)
        }
        Set-SupervisorState ("probe-" + $(if ($Healthy) { 'healthy' } else { 'degraded' })) ([pscustomobject]@{ isExpected = $isExpected; probe = $last })
        if ($isExpected) { return $last }
        Start-Sleep -Milliseconds 500
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw [TimeoutException]::new("/ready did not reach the expected $Healthy state")
}

function Quote-Arg([string]$Value) { return '"' + $Value.Replace('"', '\"') + '"' }

if (-not [string]::Equals($env:AEGISWAF_ENABLE_PG_FAULT_INJECTION, '1', [StringComparison]::Ordinal)) {
    Write-Error 'Set AEGISWAF_ENABLE_PG_FAULT_INJECTION=1 to opt in'
    exit 1
}
if (-not $ManagePostgres) {
    Write-Error 'Pass -ManagePostgres to grant explicit PostgreSQL lifecycle ownership'
    exit 1
}

try {
    if (-not [string]::Equals($env:AEGISWAF_ENABLE_PG_FAULT_INJECTION, '1', [StringComparison]::Ordinal)) {
        throw [InvalidOperationException]::new('Set AEGISWAF_ENABLE_PG_FAULT_INJECTION=1 to opt in')
    }
    if (-not $ManagePostgres) { throw [InvalidOperationException]::new('Pass -ManagePostgres to grant explicit PostgreSQL lifecycle ownership') }
    if (-not (Test-Path -LiteralPath $PgCtlPath)) { throw [FileNotFoundException]::new('pg_ctl was not found') }
    if (-not (Test-Path -LiteralPath $DataDirectory)) { throw [DirectoryNotFoundException]::new('PostgreSQL data directory was not found') }
    if (-not (Test-Path -LiteralPath $runnerExe)) { throw [FileNotFoundException]::new('Build the fault-injection runner before starting the supervisor') }
    if ($ReadyTimeoutSeconds -lt 5 -or $OutageTimeoutSeconds -lt 5 -or $RecoveryTimeoutSeconds -lt 5) { throw [ArgumentOutOfRangeException]::new('Timeouts must be at least five seconds') }

    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
    $initialListening = Test-PostgresListening
    if (-not $initialListening) {
        $started = Invoke-PgCtl @('-D', $DataDirectory, '-t', '15', '-w', 'start')
        if ($started.exitCode -ne 0 -and (Remove-StalePostmasterLock)) {
            Add-Phase 'postgres-stale-lock-recovery' 'passed' ([pscustomobject]@{ removed = 'postmaster.pid' })
            $started = Invoke-PgCtl @('-D', $DataDirectory, '-t', '15', '-w', 'start')
        }
        if ($started.exitCode -ne 0) { throw [InvalidOperationException]::new('PostgreSQL did not start') }
        $ownedPostgres = $true
        Add-Phase 'postgres-start' 'passed' $started
    } else { Add-Phase 'postgres-precondition' 'passed' ([pscustomobject]@{ listening = $true }) }

    $oldOptIn = $env:AEGISWAF_ENABLE_PG_FAULT_INJECTION
    $env:AEGISWAF_ENABLE_PG_FAULT_INJECTION = '1'
    $arguments = @('--status-file', (Quote-Arg $statusFile), '--stop-sentinel', (Quote-Arg $stopSentinel)) -join ' '
    $runner = Start-Process -FilePath $runnerExe -ArgumentList $arguments -WorkingDirectory (Split-Path -Parent $runnerExe) -PassThru -RedirectStandardOutput $runnerStdout -RedirectStandardError $runnerStderr
    if ($null -eq $oldOptIn) { Remove-Item Env:AEGISWAF_ENABLE_PG_FAULT_INJECTION -ErrorAction SilentlyContinue } else { $env:AEGISWAF_ENABLE_PG_FAULT_INJECTION = $oldOptIn }
    Set-SupervisorState 'waiting-runner-ready'
    $runnerStatus = Wait-RunnerReady $ReadyTimeoutSeconds
    Add-Phase 'runner-start' 'passed' ([pscustomobject]@{ port = $runnerStatus.port; runnerPid = $runner.Id })

    Set-SupervisorState 'waiting-ready-before-outage'
    $initial = Wait-ReadyState ([int]$runnerStatus.port) $true $ReadyTimeoutSeconds
    Add-Phase 'ready-before-outage' 'passed' $initial

    Set-SupervisorState 'stopping-postgres'
    $stopped = Invoke-PgCtl @('-D', $DataDirectory, '-t', '15', '-m', 'fast', '-w', 'stop')
    if ($stopped.exitCode -ne 0) { throw [InvalidOperationException]::new('PostgreSQL did not stop') }
    Add-Phase 'postgres-outage' 'passed' $stopped
    Set-SupervisorState 'waiting-ready-during-outage'
    $degraded = Wait-ReadyState ([int]$runnerStatus.port) $false $OutageTimeoutSeconds
    Add-Phase 'ready-during-outage' 'passed' $degraded

    $startedAgain = Invoke-PgCtl @('-D', $DataDirectory, '-t', '15', '-w', 'start')
    if ($startedAgain.exitCode -ne 0) { throw [InvalidOperationException]::new('PostgreSQL did not restart') }
    Add-Phase 'postgres-restart' 'passed' $startedAgain
    Set-SupervisorState 'waiting-ready-after-recovery'
    $recovered = Wait-ReadyState ([int]$runnerStatus.port) $true $RecoveryTimeoutSeconds
    Add-Phase 'ready-after-recovery' 'passed' $recovered
    Set-SupervisorState 'signalling-runner-stop'
    $success = $true
}
catch {
    $failureType = $_.Exception.GetType().Name
    Add-Phase 'supervisor' 'failed' ([pscustomobject]@{
            errorType = $failureType
            message = $_.Exception.Message
            lastReadyProbe = $lastReadyProbe
            runnerStatus = Get-RunnerStatus
            runnerStdout = Get-SafeDiagnostic $runnerStdout
            runnerStderr = Get-SafeDiagnostic $runnerStderr
        })
}
finally {
    if (-not (Test-Path -LiteralPath $tempRoot)) { New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null }
    try {
        New-Item -ItemType File -Path $stopSentinel -Force | Out-Null
        if ($runner) {
            $deadline = [DateTimeOffset]::UtcNow.AddSeconds(30)
            do { Start-Sleep -Milliseconds 250; $runner.Refresh() } while (-not $runner.HasExited -and [DateTimeOffset]::UtcNow -lt $deadline)
            if (-not $runner.HasExited) { Stop-Process -Id $runner.Id -Force -ErrorAction SilentlyContinue; Add-Phase 'runner-cleanup' 'failed' ([pscustomobject]@{ reason = 'timeout' }) }
            else { Add-Phase 'runner-cleanup' 'passed' ([pscustomobject]@{ exitCode = $runner.ExitCode }) }
        }
    } catch { Add-Phase 'runner-cleanup' 'failed' ([pscustomobject]@{ errorType = $_.Exception.GetType().Name }) }

    try {
        $finalListening = Test-PostgresListening
        if ($initialListening -and -not $finalListening) {
            $restored = Invoke-PgCtl @('-D', $DataDirectory, '-t', '15', '-w', 'start')
            $finalListening = Test-PostgresListening
            Add-Phase 'postgres-state-restore' ($(if ($restored.exitCode -eq 0 -and $finalListening) { 'passed' } else { 'failed' })) $restored
        } elseif (-not $initialListening -and $ownedPostgres -and $finalListening) {
            $stopped = Invoke-PgCtl @('-D', $DataDirectory, '-t', '15', '-m', 'fast', '-w', 'stop')
            $finalListening = Test-PostgresListening
            Add-Phase 'postgres-state-restore' ($(if ($stopped.exitCode -eq 0 -and -not $finalListening) { 'passed' } else { 'failed' })) $stopped
        } else { Add-Phase 'postgres-state-restore' 'passed' ([pscustomobject]@{ listening = $finalListening }) }
    } catch { Add-Phase 'postgres-state-restore' 'failed' ([pscustomobject]@{ errorType = $_.Exception.GetType().Name }) }

    if ([string]::IsNullOrWhiteSpace($EvidencePath)) {
        $EvidencePath = Join-Path $root ('artifacts\module-verification\' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-module49-postgres-fault-injection-final.json')
    }
    $evidenceDirectory = Split-Path -Parent $EvidencePath
    New-Item -ItemType Directory -Path $evidenceDirectory -Force | Out-Null
    $evidence = [pscustomobject]@{
        module = 49
        title = 'Deterministic PostgreSQL outage/reconnect supervisor'
        recordedAt = [DateTimeOffset]::UtcNow
        progress = [pscustomobject]@{ before = 0; after = $(if ($success) { 100 } else { 90 }); target = 100 }
        success = $success
        failureType = $failureType
        phases = $phases
        cleanup = [pscustomobject]@{ runnerStatus = Get-RunnerStatus; postgresListeningAtEnd = Test-PostgresListening }
        limitations = @('Opt-in local evidence only; hosted chaos/failover and external SLO review remain separate gates.')
    }
    $evidence | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $EvidencePath -Encoding UTF8
    Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}

if ($success) { Write-Output "FAULT_INJECTION_PASS evidence=$EvidencePath"; exit 0 }
Write-Error "FAULT_INJECTION_FAIL errorType=$failureType evidence=$EvidencePath"
exit 1
