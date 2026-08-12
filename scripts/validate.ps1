<#
.SYNOPSIS
    One-command validation entry point for TabDock.
.DESCRIPTION
    Builds the main app, the Spike, and both ValidationDriver projects, then
    optionally publishes the single-file executable and/or runs a real-input
    ValidationDriver scenario. All paths are resolved relative to the repo
    root. See docs/TESTING.md for the full testing playbook.
.PARAMETER Publish
    Also run the self-contained single-file publish (Release, win-x64) after
    the Debug builds.
.PARAMETER Scenario
    After building, run the ValidationDriver with `--yes <Scenario>` (e.g.
    `all` or a scenario name from docs/TESTING.md). The harness drives the
    desktop with real SendInput mouse/keyboard - do not touch the mouse or
    keyboard during a scenario run.
.EXAMPLE
    .\scripts\validate.ps1
    .\scripts\validate.ps1 -Publish
    .\scripts\validate.ps1 -Scenario all
    .\scripts\validate.ps1 -Publish -Scenario all
#>
[CmdletBinding()]
param(
    [switch]$Publish,
    [string]$Scenario
)

$ErrorActionPreference = 'Stop'

# Resolve everything relative to the repo root (one level above this script).
$RepoRoot     = Split-Path -Parent $PSScriptRoot
$MainProject  = Join-Path $RepoRoot 'TabDock.csproj'
$DebugExe     = Join-Path $RepoRoot 'bin\Debug\net8.0-windows\win-x64\TabDock.exe'
$SpikeProject = Join-Path $RepoRoot 'Spike\TabDock.Spike\TabDock.Spike.csproj'
$DriverProject = Join-Path $RepoRoot 'tests\ValidationDriver\TabDock.ValidationDriver\TabDock.ValidationDriver.csproj'
$PigProject   = Join-Path $RepoRoot 'tests\ValidationDriver\TabDock.GuineaPig\TabDock.GuineaPig.csproj'

function Invoke-Step {
    param([string]$Name, [int]$FailureExitCode, [scriptblock]$Body)

    Write-Host ''
    Write-Host "==> $Name" -ForegroundColor Cyan
    & $Body
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAILED: $Name (exit code $LASTEXITCODE)" -ForegroundColor Red
        exit $FailureExitCode
    }
}

Push-Location $RepoRoot
try {
    # (a) Main app, Debug.
    Invoke-Step 'Build TabDock.csproj (Debug)' 1 { dotnet build $MainProject }

    # (b) Survival spike, by project path.
    Invoke-Step 'Build Spike (TabDock.Spike)' 2 { dotnet build $SpikeProject }

    # (c) ValidationDriver projects. The GuineaPig MUST build without a RID:
    #     the driver resolves it at bin\Debug\net8.0-windows\TabDock.GuineaPig.exe.
    Invoke-Step 'Build TabDock.ValidationDriver' 3 { dotnet build $DriverProject }
    Invoke-Step 'Build TabDock.GuineaPig (no RID)' 4 { dotnet build $PigProject }

    # (e) CLI-safe diagnostic smoke tests. These run before any optional
    # publish/real-input work and must not create the app state file or open
    # the normal WPF UI. Start-Process is used because WinExe processes do not
    # reliably populate PowerShell's $LASTEXITCODE.
    $selfTest = Start-Process -FilePath $DebugExe -ArgumentList '--selftest-diagnostics' -NoNewWindow -Wait -PassThru
    if ($selfTest.ExitCode -ne 0) {
        Write-Host "FAILED: diagnostic self-test (exit code $($selfTest.ExitCode))" -ForegroundColor Red
        exit 5
    }

    $doctorPath = Join-Path ([IO.Path]::GetTempPath()) "TabDock-doctor-$PID-$([Guid]::NewGuid().ToString('N')).txt"
    try {
        $doctorArgs = '--doctor --output "' + $doctorPath + '"'
        $doctor = Start-Process -FilePath $DebugExe -ArgumentList $doctorArgs -NoNewWindow -Wait -PassThru
        if ($doctor.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $doctorPath)) {
            Write-Host "FAILED: doctor smoke test (exit code $($doctor.ExitCode))" -ForegroundColor Red
            exit 5
        }
    }
    finally {
        if (Test-Path -LiteralPath $doctorPath) {
            Remove-Item -LiteralPath $doctorPath -Force
        }
    }

    # (f) Optional single-file publish, as documented in AGENTS.md.
    if ($Publish) {
        $publishArgs = @(
            'publish', $MainProject,
            '-c', 'Release',
            '-r', 'win-x64',
            '--self-contained', 'true',
            '-p:PublishSingleFile=true',
            '-p:PublishReadyToRun=true',
            '-p:IncludeNativeLibrariesForSelfExtract=true'
        )
        Invoke-Step 'Publish single-file exe (Release, win-x64)' 5 { dotnet @publishArgs }
    }

    # (g) Optional real-input scenario via the ValidationDriver.
    if ($Scenario) {
        Write-Host ''
        Write-Host "==> Running ValidationDriver scenario: $Scenario" -ForegroundColor Cyan
        Write-Host 'WARNING: this harness sends REAL mouse/keyboard input (SendInput).' -ForegroundColor Yellow
        Write-Host 'Do not touch the mouse or keyboard during the run.' -ForegroundColor Yellow
        & dotnet run --project $DriverProject -- '--yes' $Scenario
        if ($LASTEXITCODE -ne 0) {
            Write-Host "FAILED: scenario '$Scenario' (exit code $LASTEXITCODE)" -ForegroundColor Red
            exit 6
        }
    }

    Write-Host ''
    Write-Host 'All requested steps completed successfully.' -ForegroundColor Green
    exit 0
}
finally {
    Pop-Location
}
