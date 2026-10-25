$ErrorActionPreference = 'Stop'

$scriptPath = Join-Path $PSScriptRoot 'verify-modules.ps1'
if (-not (Test-Path -LiteralPath $scriptPath)) {
    throw "Expected verifier at $scriptPath"
}
. $scriptPath

function Assert-Equal([object]$actual, [object]$expected, [string]$message) {
    if ($actual -ne $expected) { throw "$message. Expected '$expected', got '$actual'." }
}

function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}

$success = Invoke-Gate -Command 'Write-Output "hello world"' -TimeoutSeconds 5
Assert-Equal $success.status 'passed' 'A successful command should pass'
Assert-Equal $success.exitCode 0 'A successful command should return exit code 0'
Assert-True ($success.stdout -match 'hello world') 'The command output should be captured'

$failure = Invoke-Gate -Command 'Write-Error "expected failure"; exit 7' -TimeoutSeconds 5
Assert-Equal $failure.status 'failed' 'A non-zero command should fail'
Assert-Equal $failure.exitCode 7 'The non-zero exit code should be preserved'

$quoted = Invoke-Gate -Command 'Write-Output "quoted argument with spaces"' -TimeoutSeconds 5
Assert-Equal $quoted.status 'passed' 'Quoted arguments should remain one command'
Assert-True ($quoted.stdout -match 'quoted argument with spaces') 'Quoted command output should be captured intact'

$timeout = Invoke-Gate -Command 'Start-Sleep -Seconds 3' -TimeoutSeconds 1
Assert-Equal $timeout.status 'timeout' 'A command beyond its budget should time out'

$manifest = Get-ModuleManifest -Path (Join-Path $PSScriptRoot 'modules.json')
Assert-True ($manifest.modules.Count -ge 6) 'The manifest should define all six modules'
Assert-True ((Resolve-Module -Manifest $manifest -Id 'desktop-ui').id -eq 'desktop-ui') 'Known modules should resolve'

$unknownThrown = $false
try { Resolve-Module -Manifest $manifest -Id 'does-not-exist' | Out-Null } catch { $unknownThrown = $true }
Assert-True $unknownThrown 'Unknown modules should be rejected'

$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('aegiswaf-handoff-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
try {
    $handoffPath = Join-Path $fixtureRoot 'handoff.md'
    @'
# Handoff fixture
| โมดูล | ก่อนรอบนี้ | หลังรอบนี้ | เป้าหมาย | หลักฐานล่าสุด | สถานะ |
|---|---:|---:|---:|---|---|
| 4. Client API / Desktop UI | 0% | 30% | >98% | old evidence | กำลังทำ |

Unrelated line must remain.
'@ | Set-Content -LiteralPath $handoffPath -Encoding UTF8
    $desktop = Resolve-Module -Manifest $manifest -Id 'desktop-ui'
    $simulated = [pscustomobject]@{ id = 'desktop-ui'; name = $desktop.name; percentage = 50; passed = 2; failed = 1; unverified = 0; gates = @([pscustomobject]@{ id = 'failed-gate'; status = 'failed' }) }
    Update-Handoff -HandoffPath $handoffPath -Module $desktop -Verification $simulated -EvidencePath 'artifacts/module-verification/fixture.json'
    $updated = Get-Content -Raw -LiteralPath $handoffPath
    Assert-True ($updated -match '\| 4\. Client API / Desktop UI \| 30% \| 50% \|') 'The old after percentage should become the new before percentage'
    Assert-True ($updated -match 'Verification history') 'A verification history section should be appended'
    Assert-True ($updated -match 'Unrelated line must remain') 'Unrelated handoff content should remain'
    Assert-True ($updated -notmatch '\| 4\. Client API / Desktop UI \| 30% \| 99% \|') 'A failed gate must not be reported above the target'
} finally {
    Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
}

Write-Output 'PASS: module verifier contract tests'
