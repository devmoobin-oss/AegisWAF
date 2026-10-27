[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$writer = Join-Path $PSScriptRoot 'Write-ReleaseManifest.ps1'
if (-not (Test-Path -LiteralPath $writer)) { throw "Manifest writer not found: $writer" }

$root = Join-Path ([IO.Path]::GetTempPath()) ('aegiswaf-manifest-test-' + [Guid]::NewGuid().ToString('N'))
$bundle = Join-Path $root 'bundle'
$manifest = Join-Path $root 'manifest.json'
New-Item -ItemType Directory -Path $bundle -Force | Out-Null
try {
    Set-Content -LiteralPath (Join-Path $bundle 'AegisWAF.exe') -Value 'binary-fixture' -Encoding UTF8
    New-Item -ItemType Directory -Path (Join-Path $bundle 'assets') -Force | Out-Null
    Set-Content -LiteralPath (Join-Path (Join-Path $bundle 'assets') 'appsettings.json') -Value '{"safe":true}' -Encoding UTF8

    & $writer -InputDirectory $bundle -OutputPath $manifest -RepositoryRoot $PSScriptRoot -BuildCommand 'test-fixture' -TargetFramework 'net10.0-windows' -RuntimeIdentifier 'win-x64'
    if ($LASTEXITCODE -ne 0) { throw "Manifest writer exited with $LASTEXITCODE" }
    $document = Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
    if ($document.manifestVersion -ne 1) { throw 'Unexpected manifest version' }
    if ($document.signed -ne $false) { throw 'Unsigned fixture must be explicit' }
    if ($document.files.Count -ne 2) { throw "Expected 2 files, found $($document.files.Count)" }
    $manifestPaths = @($document.files | ForEach-Object { [string]$_.path })
    if (-not ($manifestPaths -contains 'AegisWAF.exe') -or -not ($manifestPaths -contains 'assets/appsettings.json')) {
        throw "Manifest paths are not normalized; paths=$($manifestPaths -join ', ')"
    }
    $before = ($document.files | Where-Object path -eq 'AegisWAF.exe' | Select-Object -First 1).sha256

    Add-Content -LiteralPath (Join-Path $bundle 'AegisWAF.exe') -Value 'tampered'
    & $writer -InputDirectory $bundle -OutputPath $manifest -RepositoryRoot $PSScriptRoot -BuildCommand 'test-fixture' -TargetFramework 'net10.0-windows' -RuntimeIdentifier 'win-x64'
    $after = ((Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json).files | Where-Object path -eq 'AegisWAF.exe' | Select-Object -First 1).sha256
    if ($before -eq $after) { throw 'File mutation did not change the recorded SHA-256' }
    Write-Output 'RELEASE_MANIFEST_TEST_PASS files=2 tamper_hash_changed=true'
}
finally {
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
