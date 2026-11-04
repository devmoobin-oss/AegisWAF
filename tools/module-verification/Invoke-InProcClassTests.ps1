[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [Alias('Class')]
    [string[]]$Classes,
    [switch]$SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$testProject = Join-Path $root 'tests/AegisWAF.Tests/AegisWAF.Tests.csproj'
$runnerProject = Join-Path $root 'tools/module-verification/xunit-inproc-runner/XunitInProcRunner.csproj'

$Classes = @($Classes | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($Classes.Count -eq 0) { throw 'At least one xUnit test class is required.' }

function Invoke-Dotnet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

if (-not $SkipBuild) {
    Invoke-Dotnet @('build', $testProject, '--no-restore', '--disable-build-servers', '-m:1', '--verbosity', 'minimal')
    Invoke-Dotnet @('build', $runnerProject, '--no-restore', '--disable-build-servers', '-m:1', '--verbosity', 'minimal')
}

$runArguments = @('run', '--project', $runnerProject, '--no-build', '--no-restore', '--') + $Classes
& dotnet @runArguments
exit $LASTEXITCODE
