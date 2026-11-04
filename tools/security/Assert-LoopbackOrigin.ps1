[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateRange(1, 65535)]
    [int]$Port,

    [Parameter(Mandatory = $false)]
    [string]$ListenerSnapshotPath,

    [switch]$Json
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-AddressClass {
    param([Parameter(Mandatory = $true)][string]$Address)

    $value = $Address.Trim().TrimStart('[').TrimEnd(']').ToLowerInvariant()
    if ($value -in @('127.0.0.1', '::1', '0:0:0:0:0:0:0:1')) { return 'loopback' }
    if ($value -in @('0.0.0.0', '::', '0:0:0:0:0:0:0:0')) { return 'wildcard' }
    return 'non_loopback'
}

function Convert-SnapshotEntry {
    param([Parameter(Mandatory = $true)]$Entry)

    $addressProperty = $Entry.PSObject.Properties['localAddress']
    if ($null -eq $addressProperty) { $addressProperty = $Entry.PSObject.Properties['LocalAddress'] }
    $portProperty = $Entry.PSObject.Properties['localPort']
    if ($null -eq $portProperty) { $portProperty = $Entry.PSObject.Properties['LocalPort'] }
    $stateProperty = $Entry.PSObject.Properties['state']
    if ($null -eq $stateProperty) { $stateProperty = $Entry.PSObject.Properties['State'] }
    $processProperty = $Entry.PSObject.Properties['owningProcess']
    if ($null -eq $processProperty) { $processProperty = $Entry.PSObject.Properties['OwningProcess'] }

    $address = if ($null -ne $addressProperty) { [string]$addressProperty.Value } else { '' }
    $localPort = if ($null -ne $portProperty) { [int]$portProperty.Value } else { 0 }
    $state = if ($null -ne $stateProperty) { [string]$stateProperty.Value } else { 'Listen' }
    [pscustomobject]@{
        localAddress = $address
        localPort = $localPort
        state = $state
        owningProcess = if ($null -ne $processProperty -and $null -ne $processProperty.Value) { [int]$processProperty.Value } else { $null }
    }
}

function Read-ListenerSnapshot {
    if (-not [string]::IsNullOrWhiteSpace($ListenerSnapshotPath)) {
        if (-not (Test-Path -LiteralPath $ListenerSnapshotPath -PathType Leaf)) {
            throw "Listener snapshot was not found: $ListenerSnapshotPath"
        }
        $raw = Get-Content -LiteralPath $ListenerSnapshotPath -Raw | ConvertFrom-Json
        $entries = if ($raw.PSObject.Properties.Name -contains 'listeners') { $raw.listeners } else { $raw }
        return @($entries | ForEach-Object { Convert-SnapshotEntry $_ })
    }

    $getNet = Get-Command Get-NetTCPConnection -ErrorAction SilentlyContinue
    if ($null -ne $getNet) {
        try {
            return @(Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction Stop | ForEach-Object {
                [pscustomobject]@{
                    localAddress = [string]$_.LocalAddress
                    localPort = [int]$_.LocalPort
                    state = [string]$_.State
                    owningProcess = [int]$_.OwningProcess
                }
            })
        } catch {
            # CIM access can be restricted; fall through to netstat rather than failing open or crashing.
        }
    }

    $lines = @(netstat -ano -p TCP 2>$null)
    $matches = foreach ($line in $lines) {
        if ($line -match '^\s*TCP\s+(?<local>\S+)\s+\S+\s+LISTENING\s+(?<pid>\d+)\s*$' -and
            $line -match ':\d+\s+') {
            $localEndpoint = $Matches['local']
            $separator = $localEndpoint.LastIndexOf(':')
            if ($separator -gt 0 -and [int]::TryParse($localEndpoint.Substring($separator + 1), [ref]$null)) {
                $localPort = [int]$localEndpoint.Substring($separator + 1)
                if ($localPort -eq $Port) {
                    [pscustomobject]@{
                        localAddress = $localEndpoint.Substring(0, $separator).Trim('[', ']')
                        localPort = $localPort
                        state = 'Listen'
                        owningProcess = [int]$Matches['pid']
                    }
                }
            }
        }
    }
    return @($matches)
}

$listeners = @(Read-ListenerSnapshot | Where-Object { $_.localPort -eq $Port -and $_.state -match 'Listen' })
$findings = [System.Collections.Generic.List[string]]::new()

if ($listeners.Count -eq 0) {
    $findings.Add("No LISTEN listener was found on TCP port $Port")
}

foreach ($listener in $listeners) {
    $class = Get-AddressClass ([string]$listener.localAddress)
    if ($class -eq 'wildcard') {
        $findings.Add("Wildcard listener $($listener.localAddress):$Port exposes the origin beyond loopback")
    } elseif ($class -eq 'non_loopback') {
        $findings.Add("Non-loopback listener $($listener.localAddress):$Port exposes the origin beyond loopback")
    }
}

$result = [pscustomobject]@{
    passed = $findings.Count -eq 0
    port = $Port
    listeners = @($listeners)
    findings = @($findings)
    command = "next start -H 127.0.0.1 -p $Port"
    checkedAt = [DateTimeOffset]::UtcNow.ToString('O')
}

if ($Json) {
    $result | ConvertTo-Json -Depth 6 -Compress:$false
} else {
    Write-Output ("Origin listener check: {0}" -f ($(if ($result.passed) { 'PASS' } else { 'FAIL' })))
    Write-Output ("TCP port: {0}" -f $Port)
    foreach ($finding in $findings) { Write-Output ("Finding: {0}" -f $finding) }
}

if ($result.passed) { exit 0 }
exit 1
