[CmdletBinding()]
param(
    [string]$TargetUri = 'http://127.0.0.1:5080',
    [int]$DurationSeconds = 30,
    [int]$Concurrency = 4,
    [int]$Requests = 0,
    [int]$AttackRatioPercent = 20,
    [string]$OutputJson = '.\security-loadtest.json',
    [string]$OutputCsv = '.\security-loadtest.csv'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($DurationSeconds -lt 1 -or $DurationSeconds -gt 3600) { throw 'DurationSeconds must be between 1 and 3600.' }
if ($Concurrency -lt 1 -or $Concurrency -gt 256) { throw 'Concurrency must be between 1 and 256.' }
if ($AttackRatioPercent -lt 0 -or $AttackRatioPercent -gt 100) { throw 'AttackRatioPercent must be between 0 and 100.' }

$base = $TargetUri.TrimEnd('/')
$started = [DateTimeOffset]::UtcNow
$deadline = [DateTimeOffset]::UtcNow.AddSeconds($DurationSeconds)
$results = [System.Collections.Generic.List[object]]::new()

$worker = {
    param($workerBase, $workerDeadlineText, $workerRequests, $workerAttackRatio)
    Add-Type -AssemblyName System.Net.Http
    $workerDeadline = [DateTimeOffset]::Parse($workerDeadlineText)
    $client = [System.Net.Http.HttpClient]::new()
    $client.Timeout = [TimeSpan]::FromSeconds(10)
    $local = [System.Collections.Generic.List[object]]::new()
    $number = 0
    try {
        while ([DateTimeOffset]::UtcNow -lt $workerDeadline -and ($workerRequests -le 0 -or $local.Count -lt $workerRequests)) {
            $number++
            $attack = (($number % 100) -lt $workerAttackRatio)
            $path = if ($attack) { '/load-test?q=1%20union%20select%201' } else { '/load-test?item=42' }
            $watch = [System.Diagnostics.Stopwatch]::StartNew()
            try {
                $response = $client.GetAsync($base + $path).GetAwaiter().GetResult()
                $status = [int]$response.StatusCode
                $response.Dispose()
                $local.Add([pscustomobject]@{ attack = $attack; status = $status; latencyMs = [Math]::Round($watch.Elapsed.TotalMilliseconds, 3); error = $null })
            } catch {
                $local.Add([pscustomobject]@{ attack = $attack; status = 0; latencyMs = [Math]::Round($watch.Elapsed.TotalMilliseconds, 3); error = $_.Exception.GetType().Name })
            }
        }
    } finally { $client.Dispose() }
    $local
}

$perWorkerRequests = if ($Requests -gt 0) { [Math]::Ceiling($Requests / $Concurrency) } else { 0 }
$jobs = 1..$Concurrency | ForEach-Object { Start-Job -ScriptBlock $worker -ArgumentList $base,$deadline.ToString('O'),$perWorkerRequests,$AttackRatioPercent }
Wait-Job -Job $jobs | Out-Null
foreach ($job in $jobs) {
    Receive-Job -Job $job | ForEach-Object { $results.Add($_) }
    Remove-Job -Job $job -Force
}

$items = @($results)
$latencies = @($items | Where-Object { $_.status -ne 0 } | ForEach-Object { [double]$_.latencyMs } | Sort-Object)
function Percentile([double[]]$values, [double]$p) {
    if ($values.Count -eq 0) { return 0 }
    $index = [Math]::Min($values.Count - 1, [Math]::Max(0, [int][Math]::Ceiling($values.Count * $p) - 1))
    return [Math]::Round($values[$index], 3)
}
$elapsed = ([DateTimeOffset]::UtcNow - $started).TotalSeconds
$summary = [pscustomobject]@{
    generatedAt = [DateTimeOffset]::UtcNow
    target = $TargetUri
    durationSeconds = [Math]::Round($elapsed, 3)
    concurrency = $Concurrency
    totalRequests = $items.Count
    throughputPerSecond = if ($elapsed -gt 0) { [Math]::Round($items.Count / $elapsed, 3) } else { 0 }
    blockedRequests = @($items | Where-Object { $_.status -in 403,429 }).Count
    errors = @($items | Where-Object { $_.status -eq 0 }).Count
    p50LatencyMs = Percentile $latencies 0.50
    p95LatencyMs = Percentile $latencies 0.95
    p99LatencyMs = Percentile $latencies 0.99
    statuses = @($items | Group-Object status | ForEach-Object { [pscustomobject]@{ status = [int]$_.Name; count = $_.Count } })
}
$summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $OutputJson -Encoding UTF8
$items | Export-Csv -LiteralPath $OutputCsv -NoTypeInformation -Encoding UTF8
$summary | ConvertTo-Json -Depth 5
