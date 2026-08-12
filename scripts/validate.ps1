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

function Invoke-DiagnosticProcess {
    param([string[]]$Arguments, [string]$Name)

    $process = Start-Process -FilePath $DebugExe -ArgumentList $Arguments -NoNewWindow -Wait -PassThru
    if ($process.ExitCode -ne 0) {
        Write-Host "FAILED: $Name (exit code $($process.ExitCode))" -ForegroundColor Red
        exit 5
    }
}

function Get-TreeFingerprint {
    param([string]$Root)

    if (-not (Test-Path -LiteralPath $Root)) {
        return ''
    }

    $records = foreach ($file in Get-ChildItem -LiteralPath $Root -Recurse -File | Sort-Object FullName) {
        $relative = [IO.Path]::GetRelativePath($Root, $file.FullName)
        $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        "$relative|$($file.Length)|$hash"
    }
    return ($records -join "`n")
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

    $geometrySelfTest = Start-Process -FilePath $DebugExe -ArgumentList '--selftest-geometry' -NoNewWindow -Wait -PassThru
    if ($geometrySelfTest.ExitCode -ne 0) {
        Write-Host "FAILED: geometry self-test (exit code $($geometrySelfTest.ExitCode))" -ForegroundColor Red
        exit 5
    }

    # Source-level guardrails for the two contract regressions that are not
    # safely inducible on a hosted worker. These are deliberately narrow and
    # fail closed if a future edit reintroduces the old assumptions.
    $nativeSource = Get-Content -LiteralPath (Join-Path $RepoRoot 'NativeMethods.cs') -Raw
    $shepherdSource = Get-Content -LiteralPath (Join-Path $RepoRoot 'Services\WindowShepherdService.cs') -Raw
    if ($nativeSource -match 'extern\s+bool\s+DeferWindowPos' -or
        $shepherdSource -match 'if\s*\(\s*!?\s*NativeMethods\.ShowWindow' -or
        $nativeSource -match 'DwmSetWindowAttribute') {
        Write-Host 'FAILED: native-contract source guard detected an invalid production declaration or ShowWindow/DWM mutation.' -ForegroundColor Red
        exit 5
    }

    $diagnosticRoot = Join-Path ([IO.Path]::GetTempPath()) "TabDock-validation-$PID-$([Guid]::NewGuid().ToString('N'))"
    $isolatedAppData = Join-Path $diagnosticRoot 'AppData\Roaming'
    $isolatedLocalAppData = Join-Path $diagnosticRoot 'AppData\Local'
    $isolatedTemp = Join-Path $diagnosticRoot 'Temp'
    New-Item -ItemType Directory -Force -Path $isolatedAppData, $isolatedLocalAppData, $isolatedTemp | Out-Null
    $originalAppData = [Environment]::GetEnvironmentVariable('APPDATA', 'Process')
    $originalLocalAppData = [Environment]::GetEnvironmentVariable('LOCALAPPDATA', 'Process')
    $originalUserProfile = [Environment]::GetEnvironmentVariable('USERPROFILE', 'Process')
    $originalTemp = [Environment]::GetEnvironmentVariable('TEMP', 'Process')
    $originalTmp = [Environment]::GetEnvironmentVariable('TMP', 'Process')
    try {
        [Environment]::SetEnvironmentVariable('APPDATA', $isolatedAppData, 'Process')
        [Environment]::SetEnvironmentVariable('LOCALAPPDATA', $isolatedLocalAppData, 'Process')
        [Environment]::SetEnvironmentVariable('TEMP', $isolatedTemp, 'Process')
        [Environment]::SetEnvironmentVariable('TMP', $isolatedTemp, 'Process')

        $stateRoot = Join-Path $isolatedAppData 'TabDock'
        $beforeDoctor = Get-TreeFingerprint $stateRoot
        $doctorPath = Join-Path $diagnosticRoot 'doctor.txt'
        Invoke-DiagnosticProcess @('--doctor', '--output', $doctorPath) 'doctor smoke test'
        if (-not (Test-Path -LiteralPath $doctorPath)) {
            Write-Host 'FAILED: doctor did not create its explicit output file.' -ForegroundColor Red
            exit 5
        }
        $afterDoctor = Get-TreeFingerprint $stateRoot
        if ($beforeDoctor -cne $afterDoctor) {
            Write-Host 'FAILED: doctor changed the isolated TabDock state tree.' -ForegroundColor Red
            exit 5
        }
        Write-Host 'doctor no-state-mutation: PASS' -ForegroundColor Green

        $supportPath = Join-Path $diagnosticRoot 'support-bundle.zip'
        Invoke-DiagnosticProcess @('--support-bundle', '--output', $supportPath) 'support-bundle export'
        if (-not (Test-Path -LiteralPath $supportPath)) {
            Write-Host 'FAILED: support-bundle export did not create its explicit ZIP.' -ForegroundColor Red
            exit 5
        }

        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $archive = [IO.Compression.ZipFile]::OpenRead($supportPath)
        try {
            $bundleText = foreach ($entry in $archive.Entries) {
                if ($entry.Length -eq 0) { continue }
                $reader = [IO.StreamReader]::new($entry.Open())
                try { "[$($entry.FullName)]`n$($reader.ReadToEnd())" }
                finally { $reader.Dispose() }
            }
            $bundleText = $bundleText -join "`n"
            $sensitiveTokens = @(
                [Environment]::UserName,
                [Environment]::MachineName,
                $originalAppData,
                $originalLocalAppData,
                $originalUserProfile,
                $originalTemp,
                $originalTmp,
                'password=', 'access_token', 'refresh_token', 'Bearer ', 'secret='
            ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
            foreach ($token in $sensitiveTokens) {
                if ($bundleText.IndexOf($token, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                    Write-Host "FAILED: support bundle contains a forbidden raw token/path: $token" -ForegroundColor Red
                    exit 5
                }
            }
            if ($bundleText -match '(?i)(^|\s)title\s*=\s*[^<\r\n]+' -or
                $bundleText -match '(?i)DwmSetWindowAttribute') {
                Write-Host 'FAILED: support bundle contains an unredacted title field or forbidden DWM setter evidence.' -ForegroundColor Red
                exit 5
            }
            Write-Host 'support-bundle privacy scan: PASS' -ForegroundColor Green
        }
        finally {
            $archive.Dispose()
        }
    }
    finally {
        [Environment]::SetEnvironmentVariable('APPDATA', $originalAppData, 'Process')
        [Environment]::SetEnvironmentVariable('LOCALAPPDATA', $originalLocalAppData, 'Process')
        [Environment]::SetEnvironmentVariable('TEMP', $originalTemp, 'Process')
        [Environment]::SetEnvironmentVariable('TMP', $originalTmp, 'Process')
        if (Test-Path -LiteralPath $diagnosticRoot) {
            Remove-Item -LiteralPath $diagnosticRoot -Recurse -Force
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
