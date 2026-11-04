[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$verifier = Join-Path $PSScriptRoot 'Assert-LoopbackOrigin.ps1'
$fixtureDirectory = Join-Path $PSScriptRoot '.origin-test-fixtures'
$null = New-Item -ItemType Directory -Path $fixtureDirectory -Force
$tempFiles = [System.Collections.Generic.List[string]]::new()

function Assert-Equal {
    param($Expected, $Actual, [string]$Message)
    if ($Expected -ne $Actual) { throw "$Message expected '$Expected' but got '$Actual'" }
}

function Invoke-Case {
    param([string]$Name, [object[]]$Listeners, [int]$ExpectedExit, [bool]$ExpectedPassed, [string]$ExpectedFinding)

    $path = Join-Path $fixtureDirectory ("aegiswaf-origin-" + [Guid]::NewGuid().ToString('N') + '.json')
    $tempFiles.Add($path)
    $stdoutPath = "$path.out"
    $stderrPath = "$path.err"
    $tempFiles.Add($stdoutPath)
    $tempFiles.Add($stderrPath)
    try {
        $snapshot = @($Listeners)
        if ($snapshot.Count -eq 0) { '[]' | Set-Content -LiteralPath $path -Encoding UTF8 }
        else { $snapshot | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $path -Encoding UTF8 }
        $process = Start-Process -FilePath 'powershell.exe' -ArgumentList @(
            '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $verifier,
            '-Port', '4000', '-ListenerSnapshotPath', $path, '-Json'
        ) -Wait -PassThru -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
        $exit = $process.ExitCode
        $output = Get-Content -LiteralPath $stdoutPath -Raw
        $stderr = Get-Content -LiteralPath $stderrPath -Raw
        if ([string]::IsNullOrWhiteSpace($output)) { throw "$Name emitted no JSON: $stderr" }
        $json = $output | ConvertFrom-Json
        Assert-Equal $ExpectedExit $exit "$Name exit code"
        Assert-Equal $ExpectedPassed ([bool]$json.passed) "$Name passed state"
        if ($ExpectedFinding) {
            if ((@($json.findings) -join "`n").IndexOf($ExpectedFinding, [StringComparison]::Ordinal) -lt 0) {
                throw "$Name did not contain expected finding '$ExpectedFinding'"
            }
        }
    } finally {
        Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
    }
}

Invoke-Case 'loopback-ipv4-and-ipv6' @(
    @{ localAddress = '127.0.0.1'; localPort = 4000; state = 'Listen' },
    @{ localAddress = '::1'; localPort = 4000; state = 'Listen' }
) 0 $true ''

Invoke-Case 'wildcard-ipv4' @(
    @{ localAddress = '0.0.0.0'; localPort = 4000; state = 'Listen' }
) 1 $false 'Wildcard listener'

Invoke-Case 'lan-address' @(
    @{ localAddress = '192.168.1.20'; localPort = 4000; state = 'Listen' }
) 1 $false 'Non-loopback listener'

Invoke-Case 'no-listener' @() 1 $false 'No LISTEN listener'

Write-Output 'LOOPBACK_ORIGIN_TEST_PASS cases=4'
Remove-Item -LiteralPath $fixtureDirectory -Recurse -Force -ErrorAction SilentlyContinue
