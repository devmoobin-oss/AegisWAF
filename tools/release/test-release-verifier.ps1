[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$writer = Join-Path $PSScriptRoot 'Write-ReleaseManifest.ps1'
$verifier = Join-Path $PSScriptRoot 'Verify-ReleaseManifest.ps1'
$root = Join-Path ([IO.Path]::GetTempPath()) ('aegiswaf-release-verifier-' + [Guid]::NewGuid().ToString('N'))
$bundle = Join-Path $root 'bundle'
$manifest = Join-Path $bundle 'release-manifest.json'
New-Item -ItemType Directory -Path (Join-Path $bundle 'assets') -Force | Out-Null
try {
    Set-Content -LiteralPath (Join-Path $bundle 'AegisWAF.exe') -Value 'binary-fixture' -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $bundle 'assets/appsettings.json') -Value '{"safe":true}' -Encoding UTF8
    & $writer -InputDirectory $bundle -OutputPath $manifest -RepositoryRoot $PSScriptRoot -BuildCommand 'test-fixture' -TargetFramework 'net10.0-windows' -RuntimeIdentifier 'win-x64'
    & $verifier -InputDirectory $bundle -ManifestPath $manifest -Json | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Initial verification exited with $LASTEXITCODE" }

    Add-Content -LiteralPath (Join-Path $bundle 'AegisWAF.exe') -Value 'tampered'
    $tamperRejected = $false
    try { & $verifier -InputDirectory $bundle -ManifestPath $manifest | Out-Null; throw 'Expected tampered artifact rejection.' }
    catch { if ($_.Exception.Message -like 'Expected tampered*') { throw }; $tamperRejected = $true }

    & $writer -InputDirectory $bundle -OutputPath $manifest -RepositoryRoot $PSScriptRoot -BuildCommand 'test-fixture' -TargetFramework 'net10.0-windows' -RuntimeIdentifier 'win-x64'
    Set-Content -LiteralPath (Join-Path $bundle 'unexpected.dll') -Value 'not in manifest' -Encoding UTF8
    $extraRejected = $false
    try { & $verifier -InputDirectory $bundle -ManifestPath $manifest | Out-Null; throw 'Expected unmanifested file rejection.' }
    catch { if ($_.Exception.Message -like 'Expected unmanifested*') { throw }; $extraRejected = $true }

    if (-not $tamperRejected -or -not $extraRejected) { throw 'Verifier negative cases were not rejected.' }
    Write-Output 'RELEASE_VERIFIER_TEST_PASS tamper_rejected=true extra_file_rejected=true'
}
finally {
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
