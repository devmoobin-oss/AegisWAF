#Requires -Version 5.1
<#
.SYNOPSIS
  Publishes AegisWAF Desktop as a self-contained win-x64 executable and packages it into a Windows Installer (.msi).

.DESCRIPTION
  Run this on a Windows machine with the .NET SDK and the WiX Toolset CLI (`dotnet tool install -g wix`) installed.
  Produces dist/AegisWAF-Setup-Build/ (the raw publish output) and dist/installer/AegisWAF-Setup-x64.msi (the installer
  to copy to the target machine).

.PARAMETER Version
  MSI ProductVersion. Defaults to the version in src/AegisWAF.Desktop/AegisWAF.Desktop.csproj.
#>
param(
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $repoRoot 'dist\AegisWAF-Setup-Build'
$installerOutDir = Join-Path $repoRoot 'dist\installer'
$msiPath = Join-Path $installerOutDir 'AegisWAF-Setup-x64.msi'
$desktopCsproj = Join-Path $repoRoot 'src\AegisWAF.Desktop\AegisWAF.Desktop.csproj'

if (-not $Version) {
    $csprojText = Get-Content $desktopCsproj -Raw
    if ($csprojText -match '<Version>([^<]+)</Version>') { $Version = $Matches[1] } else { $Version = '0.0.0' }
}

Write-Host "==> Publishing self-contained win-x64 build (v$Version)..." -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
dotnet publish $desktopCsproj `
    --configuration Release --runtime win-x64 --self-contained true `
    --disable-build-servers -m:1 `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None -p:DebugSymbols=false -p:NuGetAudit=false -p:MSBuildEnableWorkloadResolver=false `
    --output $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

Write-Host "==> Building installer (AegisWAF-Setup-x64.msi)..." -ForegroundColor Cyan
New-Item -ItemType Directory -Path $installerOutDir -Force | Out-Null
if (Test-Path $msiPath) { Remove-Item $msiPath -Force }

Push-Location $PSScriptRoot
try {
    wix build Product.wxs `
        -ext WixToolset.UI.wixext -ext WixToolset.Util.wixext -ext WixToolset.Firewall.wixext `
        -arch x64 `
        -d "SourceDir=$publishDir" -d "ProductVersion=$Version" `
        -out $msiPath
    if ($LASTEXITCODE -ne 0) { throw "wix build failed with exit code $LASTEXITCODE" }
}
finally {
    Pop-Location
}

Write-Host "==> Validating installer..." -ForegroundColor Cyan
wix msi validate $msiPath
if ($LASTEXITCODE -ne 0) { throw "wix msi validate failed with exit code $LASTEXITCODE" }

Write-Host ""
Write-Host "Setup ready: $msiPath" -ForegroundColor Green
