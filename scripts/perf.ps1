<#
.SYNOPSIS
    Runs TabDock's non-gating performance measurements and build timing probes.
.DESCRIPTION
    Builds the isolated tests/Performance runner when needed, exercises real
    DiagnosticTrace, LoggingService, CapturePickerViewModel.Refresh, and
    PersistenceService paths, and writes a machine-readable JSON report. The
    report is evidence for engineering decisions only; this script never fails
    on a performance threshold.
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [ValidateSet('all', 'trace', 'logging', 'picker', 'persistence')]
    [string]$Scenario = 'all',
    [string]$OutputPath = '' ,
    [switch]$SkipBuild,
    [switch]$IncludeBuildMatrix
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'tests\Performance\TabDock.Performance.csproj'
$runner = Join-Path $repoRoot "tests\Performance\bin\$Configuration\net8.0-windows\win-x64\TabDock.Performance.exe"
$sha = (git -C $repoRoot rev-parse HEAD).Trim()
$env:TABDOCK_PERF_CONFIGURATION = $Configuration
$env:TABDOCK_PERF_GIT_SHA = $sha
$matrixRoot = Join-Path ([IO.Path]::GetTempPath()) "TabDock-perf-build-$PID-$([Guid]::NewGuid().ToString('N'))"

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repoRoot ("artifacts\perf\perf-{0:yyyyMMdd-HHmmss-fff}-{1}.json" -f (Get-Date), $PID)
}

function Measure-CommandDuration {
    param([string]$Name, [scriptblock]$Body)

    $timer = [Diagnostics.Stopwatch]::StartNew()
    & $Body
    $exitCode = $LASTEXITCODE
    $timer.Stop()
    if ($exitCode -ne 0) {
        throw "$Name failed with exit code $exitCode"
    }
    [pscustomobject]@{
        name = $Name
        elapsedMs = [Math]::Round($timer.Elapsed.TotalMilliseconds, 3)
    }
}

$buildMeasurements = @()
Push-Location $repoRoot
try {
    if (-not $SkipBuild) {
        $buildMeasurements += Measure-CommandDuration "performance-runner-build-$Configuration" {
            dotnet build $project -c $Configuration --nologo | Out-Host
        }
    }

    if ($IncludeBuildMatrix) {
        New-Item -ItemType Directory -Path $matrixRoot -Force | Out-Null
        $restoreProjects = @(
            @{ name = 'restore-solution-locked'; path = $repoRoot + '\TabDock.sln' },
            @{ name = 'restore-validation-driver-locked'; path = $repoRoot + '\tests\ValidationDriver\TabDock.ValidationDriver\TabDock.ValidationDriver.csproj' },
            @{ name = 'restore-guinea-pig-locked'; path = $repoRoot + '\tests\ValidationDriver\TabDock.GuineaPig\TabDock.GuineaPig.csproj' }
        )
        foreach ($restoreProject in $restoreProjects) {
            $buildMeasurements += Measure-CommandDuration $restoreProject.name {
                dotnet restore $restoreProject.path -p:RestoreLockedMode=true --nologo | Out-Host
            }
        }
        $buildMeasurements += Measure-CommandDuration "build-solution-$Configuration" {
            dotnet build (Join-Path $repoRoot 'TabDock.sln') -c $Configuration --no-restore --nologo | Out-Host
        }
        $buildMeasurements += Measure-CommandDuration "build-app-$Configuration" {
            dotnet build (Join-Path $repoRoot 'TabDock.csproj') -c $Configuration --no-restore --nologo | Out-Host
        }
        $buildMeasurements += Measure-CommandDuration "build-spike-$Configuration" {
            dotnet build (Join-Path $repoRoot 'Spike\TabDock.Spike\TabDock.Spike.csproj') -c $Configuration --no-restore --nologo | Out-Host
        }
        $buildMeasurements += Measure-CommandDuration "build-validation-driver-$Configuration" {
            dotnet build (Join-Path $repoRoot 'tests\ValidationDriver\TabDock.ValidationDriver\TabDock.ValidationDriver.csproj') -c $Configuration --no-restore --nologo | Out-Host
        }
        $buildMeasurements += Measure-CommandDuration "build-guinea-pig-$Configuration" {
            dotnet build (Join-Path $repoRoot 'tests\ValidationDriver\TabDock.GuineaPig\TabDock.GuineaPig.csproj') -c $Configuration --no-restore --nologo | Out-Host
        }
        $buildMeasurements += Measure-CommandDuration 'openspec-install-locked' {
            Push-Location (Join-Path $repoRoot 'tools\openspec')
            try { npm ci --ignore-scripts | Out-Host }
            finally { Pop-Location }
        }
        $openSpecLocal = Join-Path $repoRoot 'tools\openspec\node_modules\.bin\openspec.cmd'
        $env:OPENSPEC_NO_UPDATE_CHECK = '1'
        $env:OPENSPEC_TELEMETRY = '0'
        $buildMeasurements += Measure-CommandDuration 'openspec-validation' {
            & $openSpecLocal validate --all --no-interactive | Out-Host
        }
        $publishRoot = Join-Path $matrixRoot 'publish'
        $buildMeasurements += Measure-CommandDuration 'publish-release-win-x64' {
            dotnet publish (Join-Path $repoRoot 'TabDock.csproj') -c Release -r win-x64 --self-contained true --no-restore -o $publishRoot -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:IncludeNativeLibrariesForSelfExtract=true | Out-Host
        }
    }

    if (-not (Test-Path -LiteralPath $runner -PathType Leaf)) {
        throw "Performance runner not found: $runner (build it or omit -SkipBuild)."
    }

    $timer = [Diagnostics.Stopwatch]::StartNew()
    & $runner --scenario $Scenario --output $OutputPath
    $exitCode = $LASTEXITCODE
    $timer.Stop()
    if ($exitCode -ne 0) {
        throw "Performance runner failed with exit code $exitCode"
    }

    $report = Get-Content -Raw -LiteralPath $OutputPath | ConvertFrom-Json
    $report | Add-Member -NotePropertyName buildTimings -NotePropertyValue $buildMeasurements -Force
    $report | Add-Member -NotePropertyName harnessElapsedMs -NotePropertyValue ([Math]::Round($timer.Elapsed.TotalMilliseconds, 3)) -Force
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath -Encoding utf8NoBOM

    Write-Host "Performance report: $([IO.Path]::GetFullPath($OutputPath))" -ForegroundColor Green
    Write-Host 'No performance threshold was enforced; inspect medians and tails before retaining medium-risk changes.' -ForegroundColor Yellow
}
finally {
    Pop-Location
    if (Test-Path -LiteralPath $matrixRoot) {
        Remove-Item -LiteralPath $matrixRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
