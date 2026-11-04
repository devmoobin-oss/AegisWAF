[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$InputDirectory,
    [Parameter(Mandatory = $true)]
    [string]$OutputPath,
    [string]$RepositoryRoot = '',
    [string]$BuildCommand = '',
    [string]$TargetFramework = '',
    [string]$RuntimeIdentifier = '',
    [switch]$RequireCleanGit
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$inputRoot = (Resolve-Path -LiteralPath $InputDirectory).Path.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
$outputFullPath = [IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Parent $outputFullPath
if (-not (Test-Path -LiteralPath $outputDirectory)) { New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null }

$repo = if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) { $null } else { (Resolve-Path -LiteralPath $RepositoryRoot).Path }
$commit = $null
$workingTreeClean = $null
if ($repo -and (Get-Command git -ErrorAction SilentlyContinue)) {
    try {
        $commitText = (& git -C $repo rev-parse HEAD 2>$null | Select-Object -First 1)
        if ($commitText) { $commit = $commitText.ToString().Trim() }
        $statusText = (& git -C $repo status --porcelain --untracked-files=all 2>$null | Out-String).Trim()
        $workingTreeClean = [string]::IsNullOrWhiteSpace($statusText)
    } catch {
        $commit = $null
        $workingTreeClean = $null
    }
}
if ($RequireCleanGit -and $workingTreeClean -ne $true) { throw 'Release provenance requires a clean Git working tree.' }

$rootPrefix = $inputRoot + [IO.Path]::DirectorySeparatorChar
$fileEntries = @(
    Get-ChildItem -LiteralPath $inputRoot -Recurse -File |
        Where-Object { [IO.Path]::GetFullPath($_.FullName) -ne $outputFullPath } |
        ForEach-Object {
            $relative = $_.FullName.Substring($rootPrefix.Length) -replace '\\', '/'
            $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            [ordered]@{
                path = $relative
                length = [int64]$_.Length
                sha256 = $hash
            }
        } |
        Sort-Object -Property path
)

$manifest = [ordered]@{
    manifestVersion = 1
    generatedAt = [DateTimeOffset]::UtcNow.ToString('O')
    signed = $false
    source = [ordered]@{
        commit = $commit
        workingTreeClean = $workingTreeClean
    }
    build = [ordered]@{
        command = $BuildCommand
        targetFramework = $TargetFramework
        runtimeIdentifier = $RuntimeIdentifier
    }
    files = $fileEntries
}

$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $outputFullPath -Encoding UTF8
Write-Output "RELEASE_MANIFEST_WRITTEN files=$($fileEntries.Count) signed=false path=$outputFullPath"
