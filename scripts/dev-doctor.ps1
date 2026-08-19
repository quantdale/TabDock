<#
.SYNOPSIS
    Developer environment preflight for TabDock.

.DESCRIPTION
    Verifies that the machine can build and publish TabDock from a fresh
    clone. The most common failure is a missing .NET 8 SDK: TabDock pins the
    .NET 8 SDK family in global.json, so a machine that only has the .NET 9
    SDK installed will report "A compatible .NET SDK was not found" even
    though the .NET 9 SDK is present and working. This script detects that
    class of problem and prints actionable remediation.

    Checks performed:
      * dotnet is resolvable on PATH.
      * A .NET 8.x SDK that satisfies global.json is installed.
      * Inside the repository, `dotnet --version` actually resolves to a
        compatible 8.0.x SDK (the real resolver semantics, not an assumption).
      * Node/npm are available when CI-style OpenSpec validation is wanted
        (reported, non-fatal).

    Exit codes:
      0 = environment looks good.
      1 = one or more problems were found (details printed).
      2 = could not parse global.json or run dotnet.

.PARAMETER RepoRoot
    Repository root (defaults to the parent of this script's directory).
#>
[CmdletBinding()]
param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$problems = [System.Collections.Generic.List[string]]::new()

function Add-Problem { param([string]$Message) $script:problems.Add($Message) }

function Write-Header($Text) {
    Write-Host ''
    Write-Host $Text -ForegroundColor Cyan
}

# --- dotnet on PATH ---------------------------------------------------------
Write-Header 'Checking dotnet on PATH'
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if ($null -eq $dotnet) {
    Add-Problem 'dotnet is not on PATH. Install the .NET SDK from https://dotnet.microsoft.com/download'
    foreach ($p in $problems) { Write-Host "  PROBLEM: $p" -ForegroundColor Red }
    exit 1
}
Write-Host "  dotnet: $($dotnet.Source)" -ForegroundColor Green

# --- global.json ------------------------------------------------------------
Write-Header 'Reading global.json'
$globalJsonPath = Join-Path $RepoRoot 'global.json'
if (-not (Test-Path -LiteralPath $globalJsonPath -PathType Leaf)) {
    Add-Problem 'global.json is missing from the repository root.'
    foreach ($p in $problems) { Write-Host "  PROBLEM: $p" -ForegroundColor Red }
    exit 2
}
$raw = Get-Content -LiteralPath $globalJsonPath -Raw
try {
    $globalJson = $raw | ConvertFrom-Json
}
catch {
    Add-Problem "global.json is not valid JSON: $($_.Exception.Message)"
    foreach ($p in $problems) { Write-Host "  PROBLEM: $p" -ForegroundColor Red }
    exit 2
}
$requiredVersion = $globalJson.sdk.version
$rollForward = if ($globalJson.sdk.rollForward) { $globalJson.sdk.rollForward } else { 'latestPatch' }
Write-Host "  required SDK version : $requiredVersion"
Write-Host "  rollForward          : $rollForward"

# --- installed SDKs ---------------------------------------------------------
Write-Header 'Installed SDKs'
$list = & dotnet --list-sdks 2>&1
foreach ($line in $list) { Write-Host "  $line" }
$installed8 = @($list | Where-Object { $_ -match '^(\d+\.\d+\.\d+)\s+\[' } | ForEach-Object {
        if ($_ -match '^(?<v>\d+\.\d+\.\d+)') { $Matches['v'] }
    } | Where-Object { $_.StartsWith('8.') })

if ($installed8.Count -eq 0) {
    Add-Problem "No .NET 8 SDK is installed. global.json requires $requiredVersion ($rollForward)."
}

# --- actual resolution inside the repo -------------------------------------
Write-Header 'Resolved SDK inside repository'
Push-Location $RepoRoot
try {
    $resolved = & dotnet --version 2>&1
    $resolveExit = $LASTEXITCODE
}
finally {
    Pop-Location
}
if ($resolveExit -ne 0) {
    Add-Problem "dotnet --version failed inside the repo (exit $resolveExit): $resolved"
}
else {
    Write-Host "  dotnet --version => $resolved"
    if (-not $resolved.StartsWith('8.')) {
        Add-Problem "Resolved SDK '$resolved' is not a .NET 8 SDK. global.json requires an 8.x SDK; .NET 9 alone is insufficient."
    }
}

# --- optional CI tooling ----------------------------------------------------
Write-Header 'Optional CI tooling'
$node = Get-Command node -ErrorAction SilentlyContinue
$npm = Get-Command npm -ErrorAction SilentlyContinue
if ($null -ne $node) { Write-Host "  node : $($node.Source) ($((node --version) 2>&1))" -ForegroundColor Green }
else { Write-Host '  node : not found (needed only for `validate.ps1 -Ci` OpenSpec validation)' -ForegroundColor Yellow }
if ($null -ne $npm) { Write-Host "  npm  : $($npm.Source) ($((npm --version) 2>&1))" -ForegroundColor Green }
else { Write-Host '  npm  : not found (needed only for `validate.ps1 -Ci` OpenSpec validation)' -ForegroundColor Yellow }

# --- verdict -----------------------------------------------------------------
Write-Host ''
if ($problems.Count -eq 0) {
    Write-Host 'Environment preflight PASSED.' -ForegroundColor Green
    Write-Host ''
    Write-Host 'Canonical commands:' -ForegroundColor White
    Write-Host '  Build    : dotnet build TabDock.sln -c Release --nologo' -ForegroundColor Gray
    Write-Host '  Validate : .\scripts\validate.ps1 -Configuration Release -Ci -Publish' -ForegroundColor Gray
    Write-Host '  Publish  : dotnet publish TabDock.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:IncludeNativeLibrariesForSelfExtract=true' -ForegroundColor Gray
    Write-Host '  Output   : bin\Release\net8.0-windows\win-x64\publish\TabDock.exe' -ForegroundColor Gray
    exit 0
}

Write-Host 'Environment preflight FAILED:' -ForegroundColor Red
foreach ($p in $problems) { Write-Host "  - $p" -ForegroundColor Red }

Write-Host ''
Write-Host 'Remediation:' -ForegroundColor White
Write-Host '  Install a current .NET 8 SDK side-by-side with any newer SDK' -ForegroundColor Gray
Write-Host '  (do NOT uninstall newer SDKs and do NOT retarget TabDock to .NET 9):' -ForegroundColor Gray
Write-Host '    winget install Microsoft.DotNet.SDK.8' -ForegroundColor Gray
Write-Host '  Then re-open the shell and re-run this script.' -ForegroundColor Gray
exit 1
