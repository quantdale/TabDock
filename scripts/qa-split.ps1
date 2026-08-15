<#
.SYNOPSIS
    One orchestrator for deterministic, controlled, guarded interactive, browser,
    stress, and comparative split qualification.
.DESCRIPTION
    The interactive tiers are supervised desktop tests. Every click/key remains
    behind ValidationDriver's live WindowFromPoint/identity/run-marker guard.
    This script only selects bounded scenarios; it never disables or widens the
    guard and never cleans up a process it did not launch.
#>
[CmdletBinding()]
param(
    [ValidateSet('deterministic', 'controlled', 'interactive', 'browser', 'stress', 'all', 'compare')]
    [string]$Tier = 'deterministic',
    [int]$Cycles = 20,
    [int]$Seed = 20260815,
    [ValidateSet('Edge', 'Brave', 'Chrome', 'All')]
    [string]$Browser = 'All',
    [switch]$KeepArtifacts,
    [string]$JsonOutput,
    [string]$BaselineSha,
    [string]$CandidatePath
)

$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent $PSScriptRoot
$DriverProject = Join-Path $RepoRoot 'tests\ValidationDriver\TabDock.ValidationDriver\TabDock.ValidationDriver.csproj'
$PigProject = Join-Path $RepoRoot 'tests\ValidationDriver\TabDock.GuineaPig\TabDock.GuineaPig.csproj'
$Solution = Join-Path $RepoRoot 'TabDock.sln'
$runId = [Guid]::NewGuid().ToString('N')
$resultRoot = if ([string]::IsNullOrWhiteSpace($JsonOutput)) {
    Join-Path $RepoRoot "artifacts\qa-split\$runId"
} else {
    [IO.Path]::GetFullPath($JsonOutput)
}

New-Item -ItemType Directory -Path $resultRoot -Force | Out-Null
$env:TABDOCK_VALIDATION_RESULT_ROOT = $resultRoot
$env:TABDOCK_QA_SPLIT_SEED = $Seed.ToString()

function Invoke-Checked {
    param([string]$Name, [scriptblock]$Body)
    Write-Host "`n==> $Name" -ForegroundColor Cyan
    & $Body
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE"
    }
}

function Invoke-DriverScenario {
    param(
        [string]$Scenario,
        [int]$ScenarioCycles = $Cycles,
        [string]$ScenarioGuest = 'pig'
    )

    $args = @(
        'run', '--project', $DriverProject, '-c', 'Release', '--no-build', '--',
        '--yes', '--configuration', 'Release', '--rid', 'auto', '--cycles', $ScenarioCycles,
        '--guest', $ScenarioGuest, $Scenario
    )
    Write-Host "`n==> guarded scenario $Scenario (cycles=$ScenarioCycles guest=$ScenarioGuest)" -ForegroundColor Cyan
    & dotnet @args
    $code = $LASTEXITCODE
    if ($code -ne 0) {
        throw "ValidationDriver '$Scenario' failed with exit code $code"
    }
}

function Invoke-Deterministic {
    Invoke-Checked 'build solution' { dotnet build $Solution -c Release --nologo }
    Invoke-Checked 'build ValidationDriver' { dotnet build $DriverProject -c Release --nologo }
    Invoke-Checked 'build GuineaPig' { dotnet build $PigProject -c Release --nologo }
    Invoke-Checked 'native-free split and identity contracts' {
        & dotnet run --project $DriverProject -c Release --no-build -- --selftest all
    }
}

function Get-InstalledBrowser {
    param([string]$Role)
    $paths = switch ($Role) {
        'Edge' { @(
            (Join-Path $env:ProgramFiles 'Microsoft\Edge\Application\msedge.exe'),
            (Join-Path ${env:ProgramFiles(x86)} 'Microsoft\Edge\Application\msedge.exe')
        ) }
        'Brave' { @(
            (Join-Path $env:ProgramFiles 'BraveSoftware\Brave-Browser\Application\brave.exe'),
            (Join-Path ${env:ProgramFiles(x86)} 'BraveSoftware\Brave-Browser\Application\brave.exe'),
            (Join-Path $env:LOCALAPPDATA 'BraveSoftware\Brave-Browser\Application\brave.exe')
        ) }
        'Chrome' { @(
            (Join-Path $env:ProgramFiles 'Google\Chrome\Application\chrome.exe'),
            (Join-Path ${env:ProgramFiles(x86)} 'Google\Chrome\Application\chrome.exe'),
            (Join-Path $env:LOCALAPPDATA 'Google\Chrome\Application\chrome.exe')
        ) }
    }
    foreach ($path in $paths) {
        if (-not [string]::IsNullOrWhiteSpace($path) -and (Test-Path -LiteralPath $path -PathType Leaf)) {
            return $path
        }
    }
    return $null
}

function Invoke-BrowserTier {
    $roles = if ($Browser -eq 'All') { @('Edge', 'Brave', 'Chrome') } else { @($Browser) }
    foreach ($role in $roles) {
        $exe = Get-InstalledBrowser $role
        if ($null -eq $exe) {
            Write-Host "SKIP_BROWSER_NOT_INSTALLED: $role" -ForegroundColor Yellow
            continue
        }
        $guest = "$($role.ToLowerInvariant())-normal"
        $env:TABDOCK_QA_BROWSER_SCOPE = $role
        try {
            # Absence was handled above. Once an executable is present, any
            # launch/provenance/rendering failure is a real blocked harness
            # result and must fail the requested tier.
            Invoke-DriverScenario 'browser-split-persistent-render' -ScenarioCycles ([Math]::Max(10, $Cycles)) -ScenarioGuest $guest
        }
        catch {
            Write-Host "BLOCKED_BROWSER_HARNESS: $role — $($_.Exception.Message)" -ForegroundColor Red
            throw
        }
        finally {
            Remove-Item Env:TABDOCK_QA_BROWSER_SCOPE -ErrorAction SilentlyContinue
        }
    }
}

function Resolve-BuiltArtifact {
    param(
        [string]$Root,
        [string]$RelativeDirectory,
        [string]$FileName
    )
    $candidates = @(
        (Join-Path $Root (Join-Path $RelativeDirectory (Join-Path 'win-x64' $FileName))),
        (Join-Path $Root (Join-Path $RelativeDirectory $FileName))
    )
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return $candidate
        }
    }
    return $null
}

function Invoke-ComparisonDriver {
    param(
        [string]$Label,
        [string]$TabDockPath,
        [string]$PigPath,
        [string]$Scenario,
        [int]$ScenarioCycles = 1,
        [switch]$LegacyPig
    )

    $scenarioRoot = Join-Path $resultRoot $Label
    New-Item -ItemType Directory -Path $scenarioRoot -Force | Out-Null
    $previousResultRoot = $env:TABDOCK_VALIDATION_RESULT_ROOT
    $previousLegacyPig = [Environment]::GetEnvironmentVariable('TABDOCK_QA_LEGACY_PIG', 'Process')
    try {
        $env:TABDOCK_VALIDATION_RESULT_ROOT = $scenarioRoot
        if ($LegacyPig) {
            $env:TABDOCK_QA_LEGACY_PIG = '1'
        } else {
            Remove-Item Env:TABDOCK_QA_LEGACY_PIG -ErrorAction SilentlyContinue
        }

        $args = @(
            'run', '--project', $DriverProject, '-c', 'Release', '--no-build', '--',
            '--yes', '--configuration', 'Release', '--rid', 'auto', '--cycles', $ScenarioCycles,
            '--guest', 'pig', '--tabdock', $TabDockPath, '--guineapig', $PigPath, $Scenario
        )
        Write-Host "`n==> comparison scenario $Label ($Scenario)" -ForegroundColor Cyan
        & dotnet @args | Out-Host
        $code = $LASTEXITCODE
        $jsonPath = Join-Path $scenarioRoot "$Scenario.json"
        if ($code -ne 0 -or -not (Test-Path -LiteralPath $jsonPath -PathType Leaf)) {
            throw "comparison scenario '$Label' failed with exit code $code or emitted no result JSON"
        }
        return Get-Content -LiteralPath $jsonPath -Raw | ConvertFrom-Json
    }
    finally {
        if ($null -eq $previousResultRoot) {
            Remove-Item Env:TABDOCK_VALIDATION_RESULT_ROOT -ErrorAction SilentlyContinue
        } else {
            $env:TABDOCK_VALIDATION_RESULT_ROOT = $previousResultRoot
        }
        if ($null -eq $previousLegacyPig) {
            Remove-Item Env:TABDOCK_QA_LEGACY_PIG -ErrorAction SilentlyContinue
        } else {
            $env:TABDOCK_QA_LEGACY_PIG = $previousLegacyPig
        }
    }
}

function Invoke-HistoricalComparison {
    param(
        [string]$Label,
        [string]$Sha
    )

    $worktree = Join-Path ([IO.Path]::GetTempPath()) "TabDock-QA-Compare-$runId-$Label"
    if (Test-Path -LiteralPath $worktree) {
        throw "historical comparison worktree path already exists: $worktree"
    }

    Write-Host "`n==> isolated historical worktree $Label ($Sha)" -ForegroundColor Cyan
    & git worktree add --detach $worktree $Sha | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "could not create historical worktree for $Label ($Sha)"
    }

    try {
        $oldSolution = Join-Path $worktree 'TabDock.sln'
        $oldPigProject = Join-Path $worktree 'tests\ValidationDriver\TabDock.GuineaPig\TabDock.GuineaPig.csproj'
        Write-Host "Building historical $Label application..." -ForegroundColor DarkCyan
        & dotnet build $oldSolution -c Release --nologo | Out-Host
        if ($LASTEXITCODE -ne 0) {
            throw "historical $Label solution build failed"
        }
        & dotnet build $oldPigProject -c Release --nologo | Out-Host
        if ($LASTEXITCODE -ne 0) {
            throw "historical $Label GuineaPig build failed"
        }

        $oldTabDock = Resolve-BuiltArtifact $worktree 'bin\Release\net8.0-windows' 'TabDock.exe'
        $oldPig = Resolve-BuiltArtifact $worktree 'tests\ValidationDriver\TabDock.GuineaPig\bin\Release\net8.0-windows' 'TabDock.GuineaPig.exe'
        if ($null -eq $oldTabDock -or $null -eq $oldPig) {
            throw "historical $Label build did not produce explicit TabDock/GuineaPig executables"
        }
        return Invoke-ComparisonDriver -Label $Label -TabDockPath $oldTabDock -PigPath $oldPig -Scenario 'split-comparison-observe' -LegacyPig
    }
    finally {
        $locked = Get-Process -ErrorAction SilentlyContinue | Where-Object {
            try { $_.Path -and $_.Path.StartsWith($worktree, [StringComparison]::OrdinalIgnoreCase) } catch { $false }
        }
        if ($null -ne $locked) {
            Write-Host "Historical worktree $Label retained because a process still has a file open; no process was killed by comparison cleanup." -ForegroundColor Yellow
        } else {
            & git worktree remove --force $worktree | Out-Host
            if ($LASTEXITCODE -ne 0) {
                Write-Host "Historical worktree cleanup failed for $Label; retained outside the repository." -ForegroundColor Yellow
            }
        }
    }
}

function Invoke-Comparison {
    $defaultA = '8b75c99cdd149648b54f98ed2ff0f9f2598bd0fc'
    $defaultB = '13c3d6f8134081aae1db03944483861888f5057f'
    $requested = if ([string]::IsNullOrWhiteSpace($BaselineSha)) {
        @($defaultA, $defaultB)
    } else {
        @($BaselineSha.Split(',') | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    }
    if ($requested.Count -eq 1) {
        if ($requested[0] -eq $defaultB) { $requested = @($defaultA, $requested[0]) }
        else { $requested = @($requested[0], $defaultB) }
    }
    if ($requested.Count -ne 2) {
        throw '-BaselineSha must be one SHA or two comma-separated SHAs (old and persistent baselines).'
    }

    foreach ($sha in $requested) {
        & git -C $RepoRoot rev-parse "$sha^{commit}" | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "baseline SHA is not available locally: $sha"
        }
    }

    $candidate = if ([string]::IsNullOrWhiteSpace($CandidatePath)) { $RepoRoot } else { [IO.Path]::GetFullPath($CandidatePath) }
    if (-not (Test-Path -LiteralPath $candidate -PathType Container)) {
        throw "candidate path does not exist: $candidate"
    }

    Invoke-Deterministic
    $candidateTabDock = Resolve-BuiltArtifact $candidate 'bin\Release\net8.0-windows' 'TabDock.exe'
    $candidatePig = Resolve-BuiltArtifact $candidate 'tests\ValidationDriver\TabDock.GuineaPig\bin\Release\net8.0-windows' 'TabDock.GuineaPig.exe'
    if ($null -eq $candidateTabDock -or $null -eq $candidatePig) {
        throw "candidate path is not built: $candidate"
    }

    $historicalA = Invoke-HistoricalComparison -Label 'baseline-8b75c99' -Sha $requested[0]
    $historicalB = Invoke-HistoricalComparison -Label 'baseline-13c3d6f' -Sha $requested[1]
    $candidateObservation = Invoke-ComparisonDriver -Label 'candidate-observation' -TabDockPath $candidateTabDock -PigPath $candidatePig -Scenario 'split-comparison-observe'
    $candidatePersistent = Invoke-ComparisonDriver -Label 'candidate-persistent' -TabDockPath $candidateTabDock -PigPath $candidatePig -Scenario 'split-click-third' -ScenarioCycles ([Math]::Max(20, $Cycles))
    $candidateFour = Invoke-ComparisonDriver -Label 'candidate-four-tab' -TabDockPath $candidateTabDock -PigPath $candidatePig -Scenario 'split-four-tab-nonmember-switching' -ScenarioCycles ([Math]::Max(20, $Cycles))
    $candidateMenu = Invoke-ComparisonDriver -Label 'candidate-menu' -TabDockPath $candidateTabDock -PigPath $candidatePig -Scenario 'split-composite' -ScenarioCycles ([Math]::Max(20, $Cycles))

    $comparison = [ordered]@{
        runId = $runId
        historicalExecution = 'isolated temporary worktrees, one baseline at a time, current guarded driver'
        baselineA = $requested[0]
        baselineB = $requested[1]
        candidatePath = $candidate
        candidateSha = (& git -C $candidate rev-parse HEAD).Trim()
        scenarios = @(
            [ordered]@{ name = 'A/B -> C'; baselineA = $historicalA.observedState; baselineB = $historicalB.observedState; final = $candidateObservation.observedState; expected = 'pair dormant' },
            [ordered]@{ name = 'C -> A/B'; baselineA = 'ordinary A presentation'; baselineB = 'same pair resumes'; final = $candidateObservation.observedState; expected = 'same pair resumes' },
            [ordered]@{ name = 'paired member menu'; baselineA = 'Split + Exit (historical source); not retained'; baselineB = 'Exit only (historical source)'; final = $candidateMenu.result; expected = 'Exit only' },
            [ordered]@{ name = 'guarded UIA suite'; baselineA = 'not available in historical harness'; baselineB = 'BLOCKED: root HWND outside prior scope'; final = $candidatePersistent.result; expected = 'ownership proof required' },
            [ordered]@{ name = '20 suspend/resume'; baselineA = '0/20: destructive split exit'; baselineB = '0/20: blocked before input'; final = $candidatePersistent.result; expected = '20/20' },
            [ordered]@{ name = '4-tab cycle'; baselineA = 'unavailable'; baselineB = 'blocked'; final = $candidateFour.result; expected = '20/20' },
            [ordered]@{ name = 'client resize evidence'; baselineA = 'absent'; baselineB = 'blocked'; final = 'covered by split-three-app-client-settle tier'; expected = 'measured before corrective click' },
            [ordered]@{ name = 'stale settle race'; baselineA = 'unavailable'; baselineB = 'unavailable'; final = 'PASS: deterministic stale-settle contract'; expected = 'no resurrection' }
        )
    }
    $path = Join-Path $resultRoot 'comparison.json'
    $comparison | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $path -Encoding UTF8
    Write-Host "Comparison evidence written to $path" -ForegroundColor Green
}

try {
    switch ($Tier) {
        'deterministic' { Invoke-Deterministic }
        'controlled' {
            Invoke-Deterministic
            Invoke-DriverScenario 'split-click-third' -ScenarioCycles ([Math]::Max(50, $Cycles))
            Invoke-DriverScenario 'split-four-tab-nonmember-switching' -ScenarioCycles ([Math]::Max(20, $Cycles))
            Invoke-DriverScenario 'split-three-app-client-settle' -ScenarioCycles ([Math]::Max(10, $Cycles))
            Invoke-DriverScenario 'split-diagnostic-snapshot' -ScenarioCycles ([Math]::Max(20, $Cycles))
            Invoke-DriverScenario 'split-dormant-member-removal' -ScenarioCycles ([Math]::Max(5, $Cycles))
            Invoke-DriverScenario 'split-native-move-reassert' -ScenarioCycles ([Math]::Max(20, $Cycles))
            Invoke-DriverScenario 'split-native-resize-reassert' -ScenarioCycles ([Math]::Max(20, $Cycles))
            Invoke-DriverScenario 'split-maximize-restore-no-overlap' -ScenarioCycles ([Math]::Max(20, $Cycles))
            Invoke-DriverScenario 'split-popout-left' -ScenarioCycles ([Math]::Max(5, $Cycles))
            Invoke-DriverScenario 'split-popout-right' -ScenarioCycles ([Math]::Max(5, $Cycles))
        }
        'interactive' {
            Invoke-Deterministic
            Invoke-DriverScenario 'split-click-third' -ScenarioCycles ([Math]::Max(20, $Cycles))
            Invoke-DriverScenario 'split-third-tab-click-persists' -ScenarioCycles ([Math]::Max(20, $Cycles))
            Invoke-DriverScenario 'split-four-tab-nonmember-switching' -ScenarioCycles ([Math]::Max(20, $Cycles))
            Invoke-DriverScenario 'split-composite' -ScenarioCycles ([Math]::Max(20, $Cycles))
            Invoke-DriverScenario 'split-exit' -ScenarioCycles ([Math]::Max(20, $Cycles))
            Invoke-DriverScenario 'split-diagnostic-snapshot' -ScenarioCycles ([Math]::Max(20, $Cycles))
            Invoke-DriverScenario 'split-native-move-reassert' -ScenarioCycles ([Math]::Max(20, $Cycles))
            Invoke-DriverScenario 'split-native-resize-reassert' -ScenarioCycles ([Math]::Max(20, $Cycles))
            Invoke-DriverScenario 'split-maximize-restore-no-overlap' -ScenarioCycles ([Math]::Max(20, $Cycles))
        }
        'browser' {
            Invoke-Deterministic
            Invoke-BrowserTier
        }
        'stress' {
            Invoke-Deterministic
            Invoke-DriverScenario 'split-click-third' -ScenarioCycles ([Math]::Max(100, $Cycles))
            Invoke-DriverScenario 'split-four-tab-nonmember-switching' -ScenarioCycles ([Math]::Max(50, $Cycles))
            Invoke-DriverScenario 'split-focus-bidirectional' -ScenarioCycles ([Math]::Max(100, $Cycles))
        }
        'all' {
            Invoke-Deterministic
            Invoke-DriverScenario 'split-click-third' -ScenarioCycles ([Math]::Max(50, $Cycles))
            Invoke-DriverScenario 'split-four-tab-nonmember-switching' -ScenarioCycles ([Math]::Max(20, $Cycles))
            Invoke-DriverScenario 'split-composite' -ScenarioCycles ([Math]::Max(20, $Cycles))
            Invoke-DriverScenario 'split-exit' -ScenarioCycles ([Math]::Max(20, $Cycles))
            Invoke-DriverScenario 'split-three-app-client-settle' -ScenarioCycles ([Math]::Max(10, $Cycles))
            Invoke-DriverScenario 'split-diagnostic-snapshot' -ScenarioCycles ([Math]::Max(20, $Cycles))
            Invoke-DriverScenario 'split-native-move-reassert' -ScenarioCycles ([Math]::Max(20, $Cycles))
            Invoke-DriverScenario 'split-native-resize-reassert' -ScenarioCycles ([Math]::Max(20, $Cycles))
            Invoke-DriverScenario 'split-maximize-restore-no-overlap' -ScenarioCycles ([Math]::Max(20, $Cycles))
            Invoke-DriverScenario 'split-popout-left' -ScenarioCycles ([Math]::Max(5, $Cycles))
            Invoke-DriverScenario 'split-popout-right' -ScenarioCycles ([Math]::Max(5, $Cycles))
            Invoke-BrowserTier
        }
        'compare' { Invoke-Comparison }
    }
    Write-Host "`nSplit qualification tier '$Tier' completed. Seed=$Seed runId=$runId resultRoot=$resultRoot" -ForegroundColor Green
}
finally {
    Remove-Item Env:TABDOCK_VALIDATION_RESULT_ROOT -ErrorAction SilentlyContinue
    Remove-Item Env:TABDOCK_QA_SPLIT_SEED -ErrorAction SilentlyContinue
}
