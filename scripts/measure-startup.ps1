<#
.SYNOPSIS
    Measures TabDock's real process-startup latency (non-gating).

.DESCRIPTION
    Launches a built or published TabDock executable and measures wall-clock
    time from process start to the application's own
    "TabDock startup complete." log line, repeated over several runs, then
    reports each sample plus min/median. This is engineering evidence only; the
    script never fails on a threshold, matching scripts/perf.ps1.

    The in-process performance harness in tests/Performance cannot measure
    process startup, so this script exists to keep cold/warm startup and
    ReadyToRun-vs-build differences observable.

    SAFETY: launching TabDock opens the real launcher on the interactive
    desktop and uses the real %APPDATA%\TabDock state. The script therefore
    refuses to run by default unless
      * no TabDock process is running (the single-instance lease would make a
        second instance exit immediately and the measurement would time out), and
      * %APPDATA%\TabDock\state.json contains no groups (a non-empty state would
        restore containers, and killing the measured instance could disturb
        captured windows).
    Pass -AllowNonEmptyState only when you understand that restoring and
    force-killing containers may release captured windows.

.PARAMETER Exe
    Explicit executable to measure. Defaults to the built development exe for
    -Configuration.

.PARAMETER Configuration
    Build configuration used to resolve the default development exe (Debug).

.PARAMETER Published
    Measure the self-contained publish output
    (bin\<Configuration>\net8.0-windows\win-x64\publish\TabDock.exe) instead of
    the development build.

.PARAMETER Runs
    Number of launches (default 3). The first launch after a publish/build can
    include cold file-cache effects; report the spread rather than a single run.

.PARAMETER TimeoutSeconds
    Per-run startup timeout (default 30).

.PARAMETER AllowNonEmptyState
    Skip the empty-state.json safety check.

.PARAMETER OutputPath
    Optional JSON report path. Defaults to artifacts\perf\startup-<timestamp>.json.

.EXAMPLE
    .\scripts\measure-startup.ps1
    .\scripts\measure-startup.ps1 -Published -Configuration Release -Runs 5
#>
[CmdletBinding()]
param(
    [string]$Exe = '',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$Published,
    [ValidateRange(1, 20)]
    [int]$Runs = 3,
    [ValidateRange(5, 300)]
    [int]$TimeoutSeconds = 30,
    [switch]$AllowNonEmptyState,
    [string]$OutputPath = ''
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$logPath = Join-Path $env:APPDATA 'TabDock\logs\TabDock.log'
$statePath = Join-Path $env:APPDATA 'TabDock\state.json'
$startupCompleteMarker = 'TabDock startup complete.'

if ([string]::IsNullOrWhiteSpace($Exe)) {
    $base = Join-Path $repoRoot "bin\$Configuration\net8.0-windows\win-x64"
    $Exe = if ($Published) { Join-Path $base 'publish\TabDock.exe' } else { Join-Path $base 'TabDock.exe' }
}
$Exe = (Resolve-Path -LiteralPath $Exe).Path

# --- safety preflight -------------------------------------------------------
if (Get-Process TabDock -ErrorAction SilentlyContinue) {
    throw 'TabDock is already running. Close it first: the single-instance lease would make the measured launch exit immediately.'
}

if (-not $AllowNonEmptyState -and (Test-Path -LiteralPath $statePath -PathType Leaf)) {
    try {
        $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
        $groupCount = @($state.Groups).Count
        if ($groupCount -gt 0) {
            throw "state.json contains $groupCount group(s). Refusing to restart the app and force-kill it with restored state; close TabDock normally first, or pass -AllowNonEmptyState."
        }
    }
    catch [System.Management.Automation.RuntimeException] {
        throw
    }
    catch {
        throw "state.json could not be parsed ($($_.Exception.Message)). Refusing to run a destructive measurement against unknown state."
    }
}

function Read-LogTail([string]$Path, [long]$FromOffset) {
    for ($attempt = 0; $attempt -lt 10; $attempt++) {
        try {
            $stream = [System.IO.File]::Open($Path, 'Open', 'Read', 'ReadWrite')
            try {
                $stream.Seek($FromOffset, 'Begin') | Out-Null
                $reader = New-Object System.IO.StreamReader($stream)
                try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
            }
            finally { $stream.Dispose() }
        }
        catch {
            Start-Sleep -Milliseconds 100
        }
    }
    return ''
}

function Measure-Startup([string]$Path, [int]$RunCount) {
    $samples = [System.Collections.Generic.List[object]]::new()
    for ($run = 1; $run -le $RunCount; $run++) {
        $offset = if (Test-Path -LiteralPath $logPath) { (Get-Item -LiteralPath $logPath).Length } else { 0 }
        $watch = [System.Diagnostics.Stopwatch]::StartNew()
        $process = Start-Process -FilePath $Path -PassThru
        $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
        $completed = $false
        while ((Get-Date) -lt $deadline) {
            Start-Sleep -Milliseconds 40
            if ((Read-LogTail $logPath $offset) -match [regex]::Escape($startupCompleteMarker)) {
                $completed = $true
                break
            }
        }
        $watch.Stop()

        $samples.Add([pscustomobject]@{
            run = $run
            completed = $completed
            elapsedMs = if ($completed) { [Math]::Round($watch.Elapsed.TotalMilliseconds, 1) } else { $null }
        })
        if (-not $completed) {
            Write-Warning "run $run did not reach '$startupCompleteMarker' within $TimeoutSeconds s"
        }

        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 500
    }
    return $samples
}

$samples = Measure-Startup $Exe $Runs
$valid = @($samples | Where-Object { $_.completed } | ForEach-Object { $_.elapsedMs })
$summary = [ordered]@{
    schemaVersion = 1
    exe = $Exe
    published = [bool]$Published
    configuration = $Configuration
    runs = $Runs
    completedRuns = $valid.Count
    minMs = if ($valid.Count -gt 0) { ($valid | Measure-Object -Minimum).Minimum } else { $null }
    medianMs = if ($valid.Count -gt 0) { [Math]::Round(($valid | Sort-Object)[[int][Math]::Floor($valid.Count / 2)], 1) } else { $null }
    maxMs = if ($valid.Count -gt 0) { ($valid | Measure-Object -Maximum).Maximum } else { $null }
    samples = @($samples)
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repoRoot ("artifacts\perf\startup-{0:yyyyMMdd-HHmmss-fff}.json" -f (Get-Date))
}
New-Item -ItemType Directory -Path (Split-Path -Parent $OutputPath) -Force | Out-Null
$summary | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $OutputPath -Encoding utf8

Write-Host ''
Write-Host "Startup measurement: $Exe" -ForegroundColor Cyan
foreach ($sample in $samples) {
    $value = if ($null -ne $sample.elapsedMs) { "$($sample.elapsedMs) ms" } else { 'timed out' }
    Write-Host ("  run {0}: {1}" -f $sample.run, $value)
}
if ($valid.Count -gt 0) {
    Write-Host ("  min={0} ms  median={1} ms  max={2} ms  (completed {3}/{4})" -f `
        $summary.minMs, $summary.medianMs, $summary.maxMs, $valid.Count, $Runs) -ForegroundColor Green
}
Write-Host "Report: $OutputPath" -ForegroundColor Gray
Write-Host 'No startup threshold was enforced; report the spread, not a single run.' -ForegroundColor Gray
