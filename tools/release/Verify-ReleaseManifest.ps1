[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$InputDirectory,
    [Parameter(Mandatory = $true)]
    [string]$ManifestPath,
    [switch]$RequireSigned,
    [switch]$RequireCleanSource,
    [switch]$Json
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$inputRoot = (Resolve-Path -LiteralPath $InputDirectory).Path.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
$manifestFullPath = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $ManifestPath).Path)
$manifest = Get-Content -LiteralPath $manifestFullPath -Raw | ConvertFrom-Json
if ($manifest.manifestVersion -ne 1) { throw "Unsupported release manifest version: $($manifest.manifestVersion)" }

$signed = [bool]$manifest.signed
if ($RequireSigned -and -not $signed) { throw 'Release manifest is unsigned.' }
if ($RequireCleanSource -and $manifest.source.workingTreeClean -ne $true) { throw 'Release manifest source was not built from a clean Git working tree.' }

$rootPrefix = $inputRoot + [IO.Path]::DirectorySeparatorChar
$entryPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$entries = @($manifest.files)
foreach ($entry in $entries) {
    $relative = ([string]$entry.path).Replace('\', '/')
    if ([string]::IsNullOrWhiteSpace($relative) -or [IO.Path]::IsPathRooted($relative) -or $relative -match '(^|/)\.\.(/|$)') {
        throw "Unsafe manifest path: $relative"
    }
    if (-not $entryPaths.Add($relative)) { throw "Duplicate manifest path: $relative" }

    $fullPath = [IO.Path]::GetFullPath((Join-Path $inputRoot ($relative -replace '/', [IO.Path]::DirectorySeparatorChar)))
    if (-not $fullPath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw "Manifest file is missing: $relative"
    }
    $file = Get-Item -LiteralPath $fullPath
    $actualHash = (Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ([int64]$entry.length -ne [int64]$file.Length) { throw "Manifest length mismatch: $relative" }
    if ($actualHash -ne ([string]$entry.sha256).ToLowerInvariant()) { throw "Manifest SHA-256 mismatch: $relative" }
}

$actualPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
Get-ChildItem -LiteralPath $inputRoot -Recurse -File |
    Where-Object { [IO.Path]::GetFullPath($_.FullName) -ne $manifestFullPath } |
    ForEach-Object {
        $relative = $_.FullName.Substring($rootPrefix.Length) -replace '\\', '/'
        [void]$actualPaths.Add($relative)
    }

$extra = @($actualPaths | Where-Object { -not $entryPaths.Contains($_) })
$missing = @($entryPaths | Where-Object { -not $actualPaths.Contains($_) })
if ($extra.Count -gt 0) { throw "Unmanifested bundle files: $($extra -join ', ')" }
if ($missing.Count -gt 0) { throw "Missing bundle files: $($missing -join ', ')" }

$result = [ordered]@{
    passed = $true
    files = $entries.Count
    signed = $signed
    sourceCommit = $manifest.source.commit
    workingTreeClean = $manifest.source.workingTreeClean
    manifest = $manifestFullPath
}
if ($Json) { $result | ConvertTo-Json -Depth 6 }
else { "RELEASE_MANIFEST_VERIFY_PASS files=$($entries.Count) signed=$signed path=$manifestFullPath" }
