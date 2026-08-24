<#
.SYNOPSIS
    Run an exported independent-machine qualification package.

.DESCRIPTION
    The default deterministic tier runs only native-free ValidationDriver
    self-tests and marks all topology evidence synthetic. Physical execution is
    opt-in and requires both -AllowPhysical and an externally proven
    exclusive-supervised desktop lease. The resulting machine report contains
    hashes and bounded classifications, never raw titles, URLs, or user paths.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PackageDir,
    [ValidateSet('deterministic', 'physical')][string]$Tier = 'deterministic',
    [switch]$AllowPhysical,
    [string]$OutputDir = ''
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-tooling.ps1')

$packageRoot = [IO.Path]::GetFullPath($PackageDir)
$packageCheck = Test-QualificationPackage -PackagePath $packageRoot
if (-not $packageCheck.Valid) { throw "qualification package is invalid: $($packageCheck.Failures -join '; ')" }
$package = $packageCheck.Package
$candidateRelative = $packageCheck.CandidateRelativePath
$candidatePath = $packageCheck.ArtifactMap[$candidateRelative].FullPath
$driverRelative = [string](Get-QualificationProperty $package 'driver' | ForEach-Object { $_.relativePath })
$driverPath = $packageCheck.ArtifactMap[$driverRelative].FullPath
$pigEntry = Get-QualificationProperty $package 'guineaPig'
$pigPath = $null
if ($null -ne $pigEntry) {
    $pigRelative = [string](Get-QualificationProperty $pigEntry 'relativePath')
    if ($packageCheck.ArtifactMap.ContainsKey($pigRelative)) { $pigPath = $packageCheck.ArtifactMap[$pigRelative].FullPath }
}
$candidateSha = $packageCheck.CandidateSha256
$sourceSha = [string](Get-QualificationProperty $package 'sourceCommitSha')
$packageManifestPath = Get-QualificationPackageManifestPath $packageRoot
$packageSha = Get-QualificationFileSha256 $packageManifestPath

$physicalLease = [string]$env:TABDOCK_VALIDATION_DESKTOP_LEASE -eq 'exclusive-supervised'
$physicalRunnable = $Tier -eq 'physical' -and $AllowPhysical -and $physicalLease
$capabilityBlock = if ($Tier -ne 'physical') { '' }
                   elseif ($physicalRunnable) { '' }
                   elseif (-not $AllowPhysical) { 'BLOCKED_SUPERVISED: physical tier requires -AllowPhysical' }
                   elseif (-not $physicalLease) { 'BLOCKED_SUPERVISED: TABDOCK_VALIDATION_DESKTOP_LEASE=exclusive-supervised was not proven' }
                   else { 'BLOCKED_SUPERVISED: physical qualification capability proof was incomplete' }

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    # Keep returned qualification evidence beside the immutable handoff. The
    # package verifier deliberately rejects files added inside the package
    # after export, so a machine run must not mutate the package itself.
    $OutputDir = Join-Path ([IO.Path]::GetDirectoryName($packageRoot)) 'qualification-results'
}
$outputRoot = [IO.Path]::GetFullPath($OutputDir)
$packagePrefix = $packageRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if ([string]::Equals($outputRoot, $packageRoot, [StringComparison]::OrdinalIgnoreCase) -or
    $outputRoot.StartsWith($packagePrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'machine qualification output must be outside the immutable qualification package directory'
}
if (Test-Path -LiteralPath $outputRoot) {
    if (@(Get-ChildItem -LiteralPath $outputRoot -Force).Count -gt 0) { throw "machine qualification output directory is not empty: $outputRoot" }
}
else { New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null }
$candidateOut = Join-Path $outputRoot 'candidate'
$toolOut = Join-Path $outputRoot 'tooling'
$runRoot = Join-Path $outputRoot ('runs\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $candidateOut,$toolOut,$runRoot -Force | Out-Null
$candidateName = [IO.Path]::GetFileName($candidatePath)
Copy-Item -LiteralPath $candidatePath -Destination (Join-Path $candidateOut $candidateName)
$releaseEntry = Get-QualificationProperty $package 'candidate'
$releaseRelative = [string](Get-QualificationProperty $releaseEntry 'releaseManifestRelativePath')
Copy-Item -LiteralPath $packageCheck.ArtifactMap[$releaseRelative].FullPath -Destination (Join-Path $candidateOut 'release-manifest.json')
$releaseRecord = (Read-QualificationJsonFile (Join-Path $candidateOut 'release-manifest.json')).Value
$sumSource = Join-Path $packageCheck.Root 'candidate\SHA256SUMS.txt'
if (-not (Test-Path -LiteralPath $sumSource -PathType Leaf)) { throw 'qualification package has no candidate SHA256SUMS.txt' }
Copy-Item -LiteralPath $sumSource -Destination (Join-Path $candidateOut 'SHA256SUMS.txt')
Copy-Item -LiteralPath $driverPath -Destination (Join-Path $toolOut 'TabDock.ValidationDriver.exe')
foreach ($runtimeEntry in @(Get-QualificationProperty (Get-QualificationProperty $package 'driver') 'runtimeFiles')) {
    $runtimeRelative = [string](Get-QualificationProperty $runtimeEntry 'relativePath')
    $runtimeSource = $packageCheck.ArtifactMap[$runtimeRelative].FullPath
    Copy-Item -LiteralPath $runtimeSource -Destination (Join-Path $toolOut ([IO.Path]::GetFileName($runtimeRelative)))
}
if ($null -ne $pigPath) { Copy-Item -LiteralPath $pigPath -Destination (Join-Path $toolOut 'TabDock.GuineaPig.exe') }
if ($null -ne $pigEntry) {
    foreach ($runtimeEntry in @(Get-QualificationProperty $pigEntry 'runtimeFiles')) {
        $runtimeRelative = [string](Get-QualificationProperty $runtimeEntry 'relativePath')
        $runtimeSource = $packageCheck.ArtifactMap[$runtimeRelative].FullPath
        Copy-Item -LiteralPath $runtimeSource -Destination (Join-Path $toolOut ([IO.Path]::GetFileName($runtimeRelative)))
    }
}

$planOutput = @(& $driverPath --plan physicalMixedDpi --configuration Release --rid none 2>&1)
if ($LASTEXITCODE -ne 0) { throw "ValidationDriver qualification plan failed with exit code $LASTEXITCODE" }
$planPath = Join-Path $outputRoot 'qualification-plan.json'
[IO.File]::WriteAllText($planPath, ($planOutput -join "`n"), [Text.UTF8Encoding]::new($false))
$planJson = Read-QualificationJsonFile $planPath
$plan = $planJson.Value

$nativeTemp = [IO.Path]::GetTempFileName()
$nativeExit = -1
try {
    & $candidatePath --selftest-native-abi *> $nativeTemp
    $nativeExit = $LASTEXITCODE
}
finally {
    Remove-Item -LiteralPath $nativeTemp -Force -ErrorAction SilentlyContinue
}

$oldArtifactRoot = $env:TABDOCK_VALIDATION_ARTIFACT_ROOT
$oldResultRoot = $env:TABDOCK_VALIDATION_RESULT_ROOT
$oldRunKind = $env:TABDOCK_VALIDATION_RUN_KIND
$oldGithubSha = $env:GITHUB_SHA
$oldDriverPath = $env:TABDOCK_VALIDATION_DRIVER_PATH
try {
    $env:TABDOCK_VALIDATION_ARTIFACT_ROOT = $runRoot
    Remove-Item Env:TABDOCK_VALIDATION_RESULT_ROOT -ErrorAction SilentlyContinue
    $env:TABDOCK_VALIDATION_RUN_KIND = if ($physicalRunnable) { 'all' } else { 'deterministic' }
    $env:GITHUB_SHA = $sourceSha
    $env:TABDOCK_VALIDATION_DRIVER_PATH = $driverPath
    if ($physicalRunnable) {
        if ($null -eq $pigPath) { throw 'physical qualification requires matching GuineaPig tooling in the package' }
        $driverArgs = @('--yes', '--configuration', 'Release', '--rid', 'none', '--tabdock', $candidatePath, '--guineapig', $pigPath, 'all')
    }
    else {
        $driverArgs = @('--selftest', 'all', '--configuration', 'Release', '--rid', 'none', '--tabdock', $candidatePath)
    }
    $driverTempOut = [IO.Path]::GetTempFileName()
    $driverTempErr = [IO.Path]::GetTempFileName()
    try {
        $startInfo = [Diagnostics.ProcessStartInfo]::new()
        $startInfo.FileName = $driverPath
        $startInfo.WorkingDirectory = $outputRoot
        $startInfo.UseShellExecute = $false
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true
        foreach ($argument in $driverArgs) { [void]$startInfo.ArgumentList.Add([string]$argument) }
        $process = [Diagnostics.Process]::Start($startInfo)
        if ($null -eq $process) { throw 'could not start the ValidationDriver process' }
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        [IO.File]::WriteAllText($driverTempOut, $stdoutTask.Result, [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText($driverTempErr, $stderrTask.Result, [Text.UTF8Encoding]::new($false))
        if ($process.ExitCode -ne 0) { throw "ValidationDriver exited $($process.ExitCode); machine qualification is not a PASS" }
    }
    finally {
        Remove-Item -LiteralPath $driverTempOut,$driverTempErr -Force -ErrorAction SilentlyContinue
    }
}
finally {
    if ($null -eq $oldArtifactRoot) { Remove-Item Env:TABDOCK_VALIDATION_ARTIFACT_ROOT -ErrorAction SilentlyContinue } else { $env:TABDOCK_VALIDATION_ARTIFACT_ROOT = $oldArtifactRoot }
    if ($null -eq $oldResultRoot) { Remove-Item Env:TABDOCK_VALIDATION_RESULT_ROOT -ErrorAction SilentlyContinue } else { $env:TABDOCK_VALIDATION_RESULT_ROOT = $oldResultRoot }
    if ($null -eq $oldRunKind) { Remove-Item Env:TABDOCK_VALIDATION_RUN_KIND -ErrorAction SilentlyContinue } else { $env:TABDOCK_VALIDATION_RUN_KIND = $oldRunKind }
    if ($null -eq $oldGithubSha) { Remove-Item Env:GITHUB_SHA -ErrorAction SilentlyContinue } else { $env:GITHUB_SHA = $oldGithubSha }
    if ($null -eq $oldDriverPath) { Remove-Item Env:TABDOCK_VALIDATION_DRIVER_PATH -ErrorAction SilentlyContinue } else { $env:TABDOCK_VALIDATION_DRIVER_PATH = $oldDriverPath }
}

$runManifests = @(Get-ChildItem -LiteralPath $runRoot -Recurse -File -Filter 'run-manifest.json' | ForEach-Object { $_.FullName })
if ($runManifests.Count -eq 0) { throw 'ValidationDriver produced no run-manifest.json' }
$primaryPath = $runManifests | Select-Object -First 1
$primarySummary = Get-QualificationManifestSummary $primaryPath
if (-not $primarySummary.Valid) { throw "primary run manifest is invalid: $($primarySummary.Failures -join '; ')" }
$bundlePath = Join-Path $outputRoot 'qualification-bundle.json'
$bundle = New-QualificationBundle -BundleRoot $outputRoot -OutputPath $bundlePath `
    -SourceCommitSha $sourceSha -SemanticVersion ([string](Get-QualificationProperty $releaseRecord 'semanticVersion')) `
    -CandidateArtifactPath (Join-Path $candidateOut $candidateName) `
    -ReleaseManifestPath (Join-Path $candidateOut 'release-manifest.json') `
    -DriverPath (Join-Path $toolOut 'TabDock.ValidationDriver.exe') `
    -QualificationManifestPaths $runManifests -PrimaryRunManifestPath $primaryPath `
    -StageARunId ([string](Get-QualificationProperty $releaseRecord 'workflowRunId')) `
    -CandidateArtifactName ([string](Get-QualificationProperty $releaseRecord 'candidateArtifactName')) `
    -EnvironmentClassification @{ physicalQualification = if ($physicalRunnable) { 'requested-and-permitted' } else { 'not-executed' }; capabilityBlock = $capabilityBlock } `
    -CapabilityObservations @{ physicalLease = $physicalLease; desktopLease = if ($physicalRunnable) { 'exclusive-supervised' } else { 'not-proven' } } `
    -SyntheticTopology:(-not $physicalRunnable)

$planTopology = Get-QualificationProperty $plan 'topology'
$observedDpis = @(Get-QualificationProperty $planTopology 'dpiValues') | ForEach-Object { [int]$_ }
$physicalTopologyEligible = $physicalRunnable -and
    [int](Get-QualificationProperty $planTopology 'monitorCount') -ge 2 -and
    [bool](Get-QualificationProperty $planTopology 'mixedDpi') -and
    @($observedDpis | Where-Object { $_ -ne 96 }).Count -gt 0
$topology = [ordered]@{
    syntheticTopology = -not $physicalRunnable
    replayOnly = $false
    physicalGateEligible = $physicalTopologyEligible
    source = if ($physicalRunnable) { 'native-monitor-observation' } else { 'virtual-topology-lab' }
    labGeneration = if ($physicalRunnable) { $null } else { 'virtual-topology-lab-2026-08-24-v1' }
    seed = if ($physicalRunnable) { $null } else { 20260824 }
    monitorCount = [int](Get-QualificationProperty $planTopology 'monitorCount')
    mixedDpi = [bool](Get-QualificationProperty $planTopology 'mixedDpi')
    dpiValues = $observedDpis
    negativeCoordinates = [bool](Get-QualificationProperty $planTopology 'negativeCoordinates')
}
$osInfo = Get-CimInstance Win32_OperatingSystem
$osFamily = if ([string]$osInfo.Caption -match 'Windows 11') { 'Windows 11' } elseif ([string]$osInfo.Caption -match 'Windows 10') { 'Windows 10' } else { 'Unknown' }
$report = [ordered]@{
    schemaVersion = 1
    reportKind = 'independent-machine-qualification'
    createdUtc = [DateTimeOffset]::UtcNow.ToString('O')
    sourceCommitSha = $sourceSha
    candidateSha256 = $candidateSha
    packageSha256 = $packageSha
    driverSha256 = Get-QualificationFileSha256 (Join-Path $toolOut 'TabDock.ValidationDriver.exe')
    os = [ordered]@{ family = $osFamily; build = [string]$osInfo.BuildNumber; architecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString(); processArchitecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString() }
    nativeAbi = [ordered]@{ status = if ($nativeExit -eq 0) { 'PASS' } else { 'FAIL_HARNESS' }; exitCode = $nativeExit; candidateSha256 = $candidateSha; command = '--selftest-native-abi' }
    topology = $topology
    qualification = [ordered]@{ tier = $Tier; physicalQualification = if ($physicalRunnable) { 'EXECUTED' } else { $capabilityBlock }; overall = $primarySummary.Outcome; scenarioCounts = $primarySummary.Counts; scenarioCount = $primarySummary.ScenarioCount; attemptCount = $primarySummary.AttemptCount; runManifestRelativePaths = @($runManifests | ForEach-Object { ConvertTo-QualificationRelativePath ([IO.Path]::GetRelativePath($outputRoot, $_)) }) }
    runManifestHashes = @($runManifests | ForEach-Object { [ordered]@{ relativePath = ConvertTo-QualificationRelativePath ([IO.Path]::GetRelativePath($outputRoot, $_)); sha256 = Get-QualificationFileSha256 $_ } })
    qualificationBundleRelativePath = 'qualification-bundle.json'
    qualificationBundleSha256 = $bundle.BundleSha256
    evidenceHashes = [ordered]@{ candidateSha256 = $candidateSha; driverSha256 = Get-QualificationFileSha256 (Join-Path $toolOut 'TabDock.ValidationDriver.exe'); bundleSha256 = $bundle.BundleSha256; primaryRunManifestSha256 = Get-QualificationFileSha256 $primaryPath }
    privacy = [ordered]@{ privacySafe = $true; containsRawDesktopData = $false; containsTitles = $false; containsUrls = $false; containsUserPaths = $false }
}
$reportPath = Join-Path $outputRoot 'machine-report.json'
[IO.File]::WriteAllText($reportPath, ($report | ConvertTo-Json -Depth 30), [Text.UTF8Encoding]::new($false))
$reportCheck = Test-QualificationMachineReport -ReportPath $reportPath -PackagePath $packageRoot -ExpectedSourceSha $sourceSha -ExpectedCandidateSha $candidateSha -RequirePhysicalMixedDpi:$physicalRunnable
if (-not $reportCheck.Valid) { throw "machine report failed offline verification: $($reportCheck.Failures -join '; ')" }
Write-Host "Machine report: $reportPath" -ForegroundColor Green
Write-Host "Qualification bundle: $bundlePath (sha256 $($bundle.BundleSha256))" -ForegroundColor Green
if (-not [string]::IsNullOrWhiteSpace($capabilityBlock)) { Write-Host $capabilityBlock -ForegroundColor Yellow }
