<#
.SYNOPSIS
    Qualify a retained Stage-A candidate without rebuilding or replacing it.

.DESCRIPTION
    This is the operator-facing bridge from release-manifest.json to the
    ValidationDriver qualification bundle. The candidate executable is read
    and hashed in place; it is never copied, rebuilt, signed, or replaced by
    this script. Only matching ValidationDriver/GuineaPig tooling is built or
    copied into the qualification workspace.

    Deterministic tier is safe on a normal workstation and produces a bundle
    with syntheticTopology=true. Physical/all tiers require an explicit
    exclusive-supervised desktop lease proof in the environment and the
    -AllowPhysical switch. Without that proof the request is recorded as
    BLOCKED_SUPERVISED and only native-free deterministic evidence runs.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$CandidateDir,
    [Parameter(Mandatory = $true)][string]$TabDock,
    [string]$SourceRoot = '',
    [ValidateSet('plan', 'deterministic', 'physical', 'all')][string]$Tier = 'deterministic',
    [string]$OutputDir = '',
    [switch]$AllowPhysical,
    [switch]$SkipBuildTooling
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-tooling.ps1')

if ([string]::IsNullOrWhiteSpace($SourceRoot)) { $SourceRoot = Split-Path -Parent $PSScriptRoot }
$sourceRootFull = [IO.Path]::GetFullPath($SourceRoot)
$candidateRoot = [IO.Path]::GetFullPath($CandidateDir)
$tabDockFull = [IO.Path]::GetFullPath($TabDock)
if (-not (Test-Path -LiteralPath $candidateRoot -PathType Container)) { throw "retained candidate directory is missing: $candidateRoot" }
if (-not (Test-Path -LiteralPath $tabDockFull -PathType Leaf)) { throw "exact candidate executable is missing: $tabDockFull" }

$releaseManifestPath = Join-Path $candidateRoot 'release-manifest.json'
$sumsPath = Join-Path $candidateRoot 'SHA256SUMS.txt'
if (-not (Test-Path -LiteralPath $releaseManifestPath -PathType Leaf)) { throw "retained candidate release-manifest.json is missing: $releaseManifestPath" }
if (-not (Test-Path -LiteralPath $sumsPath -PathType Leaf)) { throw "retained candidate SHA256SUMS.txt is missing: $sumsPath" }
$manifestJson = Read-QualificationJsonFile $releaseManifestPath
if (@($manifestJson.DuplicateFailures).Count -gt 0) { throw (@($manifestJson.DuplicateFailures) -join '; ') }
$manifest = $manifestJson.Value
$artifactFileName = [string](Get-QualificationProperty $manifest 'artifactFileName')
if ([string]::IsNullOrWhiteSpace($artifactFileName)) { $artifactFileName = 'TabDock.exe' }
$expectedTabDock = [IO.Path]::GetFullPath((Join-Path $candidateRoot $artifactFileName))
if (-not [string]::Equals($expectedTabDock, $tabDockFull, [StringComparison]::OrdinalIgnoreCase)) {
    throw "-TabDock must point to the retained manifest artifact '$expectedTabDock'; refusing to qualify a replacement path"
}
$sourceSha = [string](Get-QualificationProperty $manifest 'sourceCommitSha')
if ($sourceSha -notmatch '^[0-9a-fA-F]{40}$') { throw "release manifest sourceCommitSha is malformed: '$sourceSha'" }
$candidateHash = Get-QualificationFileSha256 $tabDockFull
$manifestHash = [string](Get-QualificationProperty $manifest 'artifactSha256')
if (-not [string]::Equals($candidateHash, $manifestHash, [StringComparison]::OrdinalIgnoreCase)) {
    throw "exact candidate hash $candidateHash != release-manifest artifactSha256 $manifestHash"
}
$sumRecord = Read-Sha256Sums $sumsPath
if (-not [string]::Equals($sumRecord.FileName, $artifactFileName, [StringComparison]::OrdinalIgnoreCase)) { throw "SHA256SUMS.txt names '$($sumRecord.FileName)', expected '$artifactFileName'" }
if (-not [string]::Equals($sumRecord.Hash, $candidateHash, [StringComparison]::OrdinalIgnoreCase)) { throw "SHA256SUMS.txt hash $($sumRecord.Hash) != exact candidate hash $candidateHash" }

$actualSource = (& git -C $sourceRootFull rev-parse HEAD 2>$null).Trim()
if ($LASTEXITCODE -ne 0 -or $actualSource -notmatch '^[0-9a-fA-F]{40}$') { throw "could not resolve source checkout HEAD in '$sourceRootFull'" }
if (-not [string]::Equals($actualSource, $sourceSha, [StringComparison]::OrdinalIgnoreCase)) { throw "source checkout HEAD $actualSource != candidate sourceCommitSha $sourceSha" }
$dirty = @(git -C $sourceRootFull status --porcelain)
if ($dirty.Count -gt 0) { throw "matching source checkout is dirty; refusing to bind qualification tooling to an uncommitted tree ($($dirty.Count) path(s))" }

$signingStatus = [string](Get-QualificationProperty $manifest 'signingStatus')
if ($signingStatus -eq 'SIGNED' -and -not (Test-AuthenticodeSignature $tabDockFull)) {
    throw 'release manifest claims SIGNED but the exact retained candidate fails independent Authenticode verification'
}

$version = [string](Get-QualificationProperty $manifest 'semanticVersion')
if ([string]::IsNullOrWhiteSpace($version)) { throw 'release manifest semanticVersion is missing' }
if ([string]::IsNullOrWhiteSpace($OutputDir)) { $OutputDir = Join-Path $candidateRoot 'qualification' }
$outputRoot = [IO.Path]::GetFullPath($OutputDir)
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$toolRoot = Join-Path $outputRoot 'tooling'
$runRoot = Join-Path $outputRoot ('runs\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $toolRoot,$runRoot -Force | Out-Null
$driverProject = Join-Path $sourceRootFull 'tests\ValidationDriver\TabDock.ValidationDriver\TabDock.ValidationDriver.csproj'
$pigProject = Join-Path $sourceRootFull 'tests\ValidationDriver\TabDock.GuineaPig\TabDock.GuineaPig.csproj'
$driverBuild = Join-Path $sourceRootFull 'tests\ValidationDriver\TabDock.ValidationDriver\bin\Release\net8.0-windows'
$pigBuild = Join-Path $sourceRootFull 'tests\ValidationDriver\TabDock.GuineaPig\bin\Release\net8.0-windows'
$driverBuilt = Join-Path $driverBuild 'TabDock.ValidationDriver.exe'
$pigBuilt = Join-Path $pigBuild 'TabDock.GuineaPig.exe'
if (-not $SkipBuildTooling) {
    if (-not (Test-Path -LiteralPath $driverBuilt -PathType Leaf)) {
        dotnet build $driverProject -c Release --nologo
        if ($LASTEXITCODE -ne 0) { throw 'matching ValidationDriver tooling build failed' }
    }
    if (($Tier -in @('physical', 'all')) -and -not (Test-Path -LiteralPath $pigBuilt -PathType Leaf)) {
        dotnet build $pigProject -c Release --nologo
        if ($LASTEXITCODE -ne 0) { throw 'matching GuineaPig tooling build failed' }
    }
}
if (-not (Test-Path -LiteralPath $driverBuilt -PathType Leaf)) { throw "matching ValidationDriver executable is unavailable: $driverBuilt" }
$driverPath = Join-Path $toolRoot 'TabDock.ValidationDriver.exe'
Copy-Item -LiteralPath $driverBuilt -Destination $driverPath -Force
$pigPath = $null
if (Test-Path -LiteralPath $pigBuilt -PathType Leaf) {
    $pigPath = Join-Path $toolRoot 'TabDock.GuineaPig.exe'
    Copy-Item -LiteralPath $pigBuilt -Destination $pigPath -Force
}

$physicalLease = [string]$env:TABDOCK_VALIDATION_DESKTOP_LEASE -eq 'exclusive-supervised'
$physicalRequested = $Tier -in @('physical', 'all')
$physicalRunnable = $physicalRequested -and $AllowPhysical -and $physicalLease
$capabilityBlock = if (-not $physicalRequested) { '' }
                   elseif ($physicalRunnable) { '' }
                   elseif (-not $AllowPhysical) { 'BLOCKED_SUPERVISED: physical tier requires -AllowPhysical' }
                   elseif (-not $physicalLease) { 'BLOCKED_SUPERVISED: TABDOCK_VALIDATION_DESKTOP_LEASE=exclusive-supervised was not proven' }
                   else { 'BLOCKED_SUPERVISED: physical qualification capability proof was incomplete' }
$requestedPlan = [ordered]@{
    tier = $Tier
    candidateRelativePath = (ConvertTo-QualificationRelativePath ([IO.Path]::GetRelativePath($candidateRoot, $tabDockFull)))
    candidateSha256 = $candidateHash
    sourceCommitSha = $sourceSha
    semanticVersion = $version
    physicalRequested = $physicalRequested
    physicalRunnable = $physicalRunnable
    capabilityBlock = $capabilityBlock
    syntheticTopology = -not $physicalRunnable
    driverRelativePath = (ConvertTo-QualificationRelativePath ([IO.Path]::GetRelativePath($candidateRoot, $driverPath)))
}
$planPath = Join-Path $outputRoot 'qualification-plan.json'
[IO.File]::WriteAllText($planPath, ($requestedPlan | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
if ($Tier -eq 'plan') {
    Write-Host "Qualification plan: $planPath" -ForegroundColor Green
    Write-Host ($requestedPlan | ConvertTo-Json -Depth 12)
    exit 0
}

$oldArtifactRoot = $env:TABDOCK_VALIDATION_ARTIFACT_ROOT
$oldResultRoot = $env:TABDOCK_VALIDATION_RESULT_ROOT
$oldRunKind = $env:TABDOCK_VALIDATION_RUN_KIND
$oldParentRun = $env:TABDOCK_VALIDATION_PARENT_RUN_ID
$oldShard = $env:TABDOCK_VALIDATION_SHARD
$oldGithubSha = $env:GITHUB_SHA
try {
    $env:TABDOCK_VALIDATION_ARTIFACT_ROOT = $runRoot
    Remove-Item Env:TABDOCK_VALIDATION_RESULT_ROOT -ErrorAction SilentlyContinue
    $env:TABDOCK_VALIDATION_RUN_KIND = if ($physicalRunnable) { 'all' } else { 'deterministic' }
    Remove-Item Env:TABDOCK_VALIDATION_PARENT_RUN_ID -ErrorAction SilentlyContinue
    Remove-Item Env:TABDOCK_VALIDATION_SHARD -ErrorAction SilentlyContinue
    $env:GITHUB_SHA = $sourceSha
    if ($physicalRunnable) {
        if ($null -eq $pigPath) { throw 'physical qualification requires a matching GuineaPig executable' }
        $driverArgs = @('--yes', '--configuration', 'Release', '--rid', 'win-x64', '--tabdock', $tabDockFull, '--guineapig', $pigPath, 'all')
    }
    else {
        $driverArgs = @('--selftest', 'all', '--configuration', 'Release', '--rid', 'none', '--tabdock', $tabDockFull)
        if ($null -ne $pigPath) { $driverArgs += @('--guineapig', $pigPath) }
    }
    $driverLog = Join-Path $outputRoot 'validation-driver.log'
    $driverErr = Join-Path $outputRoot 'validation-driver.err.log'
    $process = Start-Process -FilePath $driverPath -ArgumentList $driverArgs -WorkingDirectory $sourceRootFull -RedirectStandardOutput $driverLog -RedirectStandardError $driverErr -NoNewWindow -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "deterministic ValidationDriver exited $($process.ExitCode); see qualification/validation-driver.log" }
}
finally {
    if ($null -eq $oldArtifactRoot) { Remove-Item Env:TABDOCK_VALIDATION_ARTIFACT_ROOT -ErrorAction SilentlyContinue } else { $env:TABDOCK_VALIDATION_ARTIFACT_ROOT = $oldArtifactRoot }
    if ($null -eq $oldResultRoot) { Remove-Item Env:TABDOCK_VALIDATION_RESULT_ROOT -ErrorAction SilentlyContinue } else { $env:TABDOCK_VALIDATION_RESULT_ROOT = $oldResultRoot }
    if ($null -eq $oldRunKind) { Remove-Item Env:TABDOCK_VALIDATION_RUN_KIND -ErrorAction SilentlyContinue } else { $env:TABDOCK_VALIDATION_RUN_KIND = $oldRunKind }
    if ($null -eq $oldParentRun) { Remove-Item Env:TABDOCK_VALIDATION_PARENT_RUN_ID -ErrorAction SilentlyContinue } else { $env:TABDOCK_VALIDATION_PARENT_RUN_ID = $oldParentRun }
    if ($null -eq $oldShard) { Remove-Item Env:TABDOCK_VALIDATION_SHARD -ErrorAction SilentlyContinue } else { $env:TABDOCK_VALIDATION_SHARD = $oldShard }
    if ($null -eq $oldGithubSha) { Remove-Item Env:GITHUB_SHA -ErrorAction SilentlyContinue } else { $env:GITHUB_SHA = $oldGithubSha }
}

$runManifests = @(Get-ChildItem -LiteralPath $runRoot -Recurse -File -Filter 'run-manifest.json' | ForEach-Object { $_.FullName })
if ($runManifests.Count -eq 0) { throw "ValidationDriver produced no run-manifest.json under '$runRoot'" }
$primary = $runManifests | Select-Object -First 1
$bundlePath = Join-Path $candidateRoot 'qualification-bundle.json'
$bundle = New-QualificationBundle -BundleRoot $candidateRoot -OutputPath $bundlePath `
    -SourceCommitSha $sourceSha -SemanticVersion $version -CandidateArtifactPath $tabDockFull `
    -ReleaseManifestPath $releaseManifestPath -DriverPath $driverPath `
    -QualificationManifestPaths $runManifests -PrimaryRunManifestPath $primary `
    -StageARunId ([string](Get-QualificationProperty $manifest 'workflowRunId')) `
    -CandidateArtifactName ([string](Get-QualificationProperty $manifest 'candidateArtifactName')) `
    -EnvironmentClassification @{ physicalQualification = if ($physicalRunnable) { 'requested-and-permitted' } else { 'not-executed' }; capabilityBlock = $capabilityBlock } `
    -CapabilityObservations @{ physicalLease = $physicalLease; desktopLease = if ($physicalRunnable) { 'exclusive-supervised' } else { 'not-proven' } } `
    -SyntheticTopology:(-not $physicalRunnable)
$report = [ordered]@{
    schemaVersion = 1
    reportKind = 'candidate-qualification'
    createdUtc = [DateTimeOffset]::UtcNow.ToString('O')
    sourceCommitSha = $sourceSha
    semanticVersion = $version
    candidateSha256 = $candidateHash
    candidateRelativePath = (ConvertTo-QualificationRelativePath ([IO.Path]::GetRelativePath($candidateRoot, $tabDockFull)))
    driverSha256 = Get-QualificationFileSha256 $driverPath
    driverRelativePath = (ConvertTo-QualificationRelativePath ([IO.Path]::GetRelativePath($candidateRoot, $driverPath)))
    tier = $Tier
    physicalQualification = if ($physicalRunnable) { 'EXECUTED' } else { $capabilityBlock }
    syntheticTopology = -not $physicalRunnable
    qualificationBundleRelativePath = (ConvertTo-QualificationRelativePath ([IO.Path]::GetRelativePath($candidateRoot, $bundle.BundlePath)))
    qualificationBundleSha256 = $bundle.BundleSha256
    runManifestRelativePaths = @($runManifests | ForEach-Object { ConvertTo-QualificationRelativePath ([IO.Path]::GetRelativePath($candidateRoot, $_)) })
}
$reportPath = Join-Path $outputRoot 'qualification-report.json'
[IO.File]::WriteAllText($reportPath, ($report | ConvertTo-Json -Depth 20), [Text.UTF8Encoding]::new($false))
Write-Host "Exact candidate SHA-256: $candidateHash" -ForegroundColor Green
Write-Host "Qualification bundle: $($bundle.BundlePath) (sha256 $($bundle.BundleSha256))" -ForegroundColor Green
Write-Host "Qualification report: $reportPath" -ForegroundColor Green
if (-not [string]::IsNullOrWhiteSpace($capabilityBlock)) { Write-Host $capabilityBlock -ForegroundColor Yellow }
