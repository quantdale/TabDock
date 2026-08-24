<#
.SYNOPSIS
    Merge validated independent-machine reports into release evidence.

.DESCRIPTION
    This command is the originating-machine side of the Windows handoff. It
    treats returned reports and bundles as untrusted data, validates them
    against the exact retained candidate and matching handoff package, copies
    only the bundle-indexed evidence into the candidate artifact directory,
    and writes schema-3 release-external-evidence.json. It never launches a
    returned executable or script and never changes the candidate bytes.

    A complete merge requires one validated physical mixed-DPI report and
    validated Windows 10 and Windows 11 reports. The existing final human
    smoke attestation is preserved from the input evidence file; this command
    does not create or replace that attestation.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ArtifactDir,
    [Parameter(Mandatory = $true)][string]$EvidencePath,
    [Parameter(Mandatory = $true)][string]$PackagePath,
    [Parameter(Mandatory = $true)][string[]]$MachineReportPath,
    [string]$OutputPath = '',
    [string]$HandoffDir = '',
    [Parameter(Mandatory = $true)][string]$Operator
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-tooling.ps1')

function Set-MergeProperty {
    param(
        [Parameter(Mandatory = $true)][object]$Object,
        [Parameter(Mandatory = $true)][string]$Name,
        [AllowNull()][object]$Value
    )
    if ($null -eq $Object) { throw "cannot set '$Name' on a null evidence object" }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { $Object | Add-Member -NotePropertyName $Name -NotePropertyValue $Value -Force }
    else { $property.Value = $Value }
}

function Copy-ValidatedQualificationFile {
    param(
        [Parameter(Mandatory = $true)][string]$SourcePath,
        [Parameter(Mandatory = $true)][string]$DestinationPath
    )
    if (-not (Test-Path -LiteralPath $SourcePath -PathType Leaf)) { throw "validated qualification artifact is missing: $SourcePath" }
    if (Test-Path -LiteralPath $DestinationPath -PathType Leaf) {
        $sourceHash = Get-QualificationFileSha256 $SourcePath
        $destinationHash = Get-QualificationFileSha256 $DestinationPath
        if (-not [string]::Equals($sourceHash, $destinationHash, [StringComparison]::OrdinalIgnoreCase)) {
            throw "refusing to overwrite a different staged qualification artifact: $DestinationPath"
        }
        return
    }
    $parent = [IO.Path]::GetDirectoryName($DestinationPath)
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
    Copy-Item -LiteralPath $SourcePath -Destination $DestinationPath
}

function Get-QualificationPrimaryManifestHash {
    param([Parameter(Mandatory = $true)][object]$Bundle)
    $primary = [string](Get-QualificationProperty $Bundle 'primaryRunManifest')
    $entry = @((Get-QualificationProperty $Bundle 'runManifests') | Where-Object {
            [string](Get-QualificationProperty $_ 'relativePath') -eq $primary
        }) | Select-Object -First 1
    if ($null -eq $entry) { throw 'qualification bundle has no indexed primary run manifest' }
    return [string](Get-QualificationProperty $entry 'sha256')
}

function Stage-QualificationReport {
    param(
        [Parameter(Mandatory = $true)][object]$ReportResult,
        [Parameter(Mandatory = $true)][string]$ReportPath,
        [Parameter(Mandatory = $true)][string]$ArtifactRoot,
        [Parameter(Mandatory = $true)][string]$ReportSha
    )
    $reportFull = [IO.Path]::GetFullPath($ReportPath)
    $reportRoot = [IO.Path]::GetDirectoryName($reportFull)
    $report = $ReportResult.Report
    $bundleRelativeFromReport = [string](Get-QualificationProperty $report 'qualificationBundleRelativePath')
    if ([string]::IsNullOrWhiteSpace($bundleRelativeFromReport)) { throw "machine report '$ReportPath' has no qualificationBundleRelativePath" }
    $bundleFull = (Resolve-QualificationRelativePath -Root $reportRoot -RelativePath $bundleRelativeFromReport).FullPath
    $bundleCheck = Test-QualificationBundle -BundlePath $bundleFull -ExpectedSourceSha ([string](Get-QualificationProperty $report 'sourceCommitSha')) -ExpectedArtifactSha ([string](Get-QualificationProperty $report 'candidateSha256'))
    if (-not $bundleCheck.Valid) { throw "machine report bundle is invalid: $($bundleCheck.Failures -join '; ')" }
    $bundle = $bundleCheck.Bundle
    $shortHash = $ReportSha.Substring(0, 12).ToLowerInvariant()
    $osSlug = ([string](Get-QualificationProperty (Get-QualificationProperty $report 'os') 'family')).ToLowerInvariant().Replace(' ', '')
    if ([string]::IsNullOrWhiteSpace($osSlug)) { $osSlug = ([string](Get-QualificationProperty $report 'osFamily')).ToLowerInvariant().Replace(' ', '') }
    if ([string]::IsNullOrWhiteSpace($osSlug)) { throw "machine report '$ReportPath' has no OS family" }
    $stageRoot = Join-Path $ArtifactRoot ("qualification\external\$osSlug-$shortHash")
    $bundleRoot = [IO.Path]::GetDirectoryName($bundleFull)
    $bundleRootRelative = [IO.Path]::GetRelativePath($reportRoot, $bundleRoot)
    $stageBundleRoot = if ([string]::Equals($bundleRootRelative, '.', [StringComparison]::Ordinal)) { $stageRoot } else { Join-Path $stageRoot $bundleRootRelative }

    foreach ($entry in @(Get-QualificationProperty $bundle 'artifactIndex')) {
        $source = (Resolve-QualificationRelativePath -Root $bundleRoot -RelativePath ([string](Get-QualificationProperty $entry 'relativePath'))).FullPath
        $relativeFromReport = (Resolve-QualificationRelativePath -Root $reportRoot -RelativePath ([IO.Path]::GetRelativePath($reportRoot, $source))).RelativePath
        Copy-ValidatedQualificationFile -SourcePath $source -DestinationPath (Join-Path $stageRoot ($relativeFromReport -replace '/', '\'))
    }
    $bundleRelativeFromBundleRoot = [IO.Path]::GetRelativePath($bundleRoot, $bundleFull)
    Copy-ValidatedQualificationFile -SourcePath $bundleFull -DestinationPath (Join-Path $stageBundleRoot ($bundleRelativeFromBundleRoot -replace '/', '\'))
    $reportRelative = (Resolve-QualificationRelativePath -Root $reportRoot -RelativePath ([IO.Path]::GetFileName($reportFull))).RelativePath
    $stagedReportPath = Join-Path $stageRoot ($reportRelative -replace '/', '\')
    Copy-ValidatedQualificationFile -SourcePath $reportFull -DestinationPath $stagedReportPath
    $stagedBundlePath = Join-Path $stageBundleRoot ($bundleRelativeFromBundleRoot -replace '/', '\')
    $stagedReportRelative = (Resolve-QualificationRelativePath -Root $ArtifactRoot -RelativePath ([IO.Path]::GetRelativePath($ArtifactRoot, $stagedReportPath))).RelativePath
    $stagedBundleRelative = (Resolve-QualificationRelativePath -Root $ArtifactRoot -RelativePath ([IO.Path]::GetRelativePath($ArtifactRoot, $stagedBundlePath))).RelativePath
    [pscustomobject]@{
        Report = $report
        Bundle = $bundle
        ReportPath = $stagedReportPath
        ReportRelativePath = $stagedReportRelative
        ReportSha256 = Get-QualificationFileSha256 $stagedReportPath
        BundlePath = $stagedBundlePath
        BundleRelativePath = $stagedBundleRelative
        BundleSha256 = Get-QualificationFileSha256 $stagedBundlePath
        PrimaryRunManifestSha256 = Get-QualificationPrimaryManifestHash $bundle
    }
}

$artifactRoot = [IO.Path]::GetFullPath($ArtifactDir)
if (-not (Test-Path -LiteralPath $artifactRoot -PathType Container)) { throw "candidate artifact directory is missing: $artifactRoot" }
$releasePath = Join-Path $artifactRoot 'release-manifest.json'
if (-not (Test-Path -LiteralPath $releasePath -PathType Leaf)) { throw "release-manifest.json is missing: $releasePath" }
$releaseJson = Read-QualificationJsonFile $releasePath
if (@($releaseJson.DuplicateFailures).Count -gt 0) { throw (@($releaseJson.DuplicateFailures) -join '; ') }
$release = $releaseJson.Value
$artifactName = [string](Get-QualificationProperty $release 'artifactFileName')
if ([string]::IsNullOrWhiteSpace($artifactName)) { $artifactName = 'TabDock.exe' }
$candidatePath = Join-Path $artifactRoot $artifactName
if (-not (Test-Path -LiteralPath $candidatePath -PathType Leaf)) { throw "candidate executable is missing: $candidatePath" }
$candidateSha = Get-QualificationFileSha256 $candidatePath
$sourceSha = [string](Get-QualificationProperty $release 'sourceCommitSha')
if ($sourceSha -notmatch '^[0-9a-fA-F]{40}$') { throw 'release manifest sourceCommitSha is malformed' }
if (-not [string]::Equals($candidateSha, [string](Get-QualificationProperty $release 'artifactSha256'), [StringComparison]::OrdinalIgnoreCase)) { throw 'release manifest artifactSha256 does not match the retained candidate' }

$evidenceFull = [IO.Path]::GetFullPath($EvidencePath)
if (-not (Test-Path -LiteralPath $evidenceFull -PathType Leaf)) { throw "external evidence file is missing: $evidenceFull" }
$evidenceJson = Read-QualificationJsonFile $evidenceFull
if (@($evidenceJson.DuplicateFailures).Count -gt 0) { throw (@($evidenceJson.DuplicateFailures) -join '; ') }
$evidence = $evidenceJson.Value
if (-not [string]::Equals([string](Get-QualificationProperty $evidence 'sourceCommitSha'), $sourceSha, [StringComparison]::OrdinalIgnoreCase)) { throw 'input external evidence sourceCommitSha disagrees with the retained candidate' }
if (-not [string]::Equals([string](Get-QualificationProperty $evidence 'artifactSha256'), $candidateSha, [StringComparison]::OrdinalIgnoreCase)) { throw 'input external evidence artifactSha256 disagrees with the retained candidate' }
Set-MergeProperty -Object $evidence -Name 'schemaVersion' -Value 3

$packageCheck = Test-QualificationPackage -PackagePath $PackagePath -ExpectedSourceSha $sourceSha -ExpectedCandidateSha $candidateSha
if (-not $packageCheck.Valid) { throw "qualification handoff package is invalid: $($packageCheck.Failures -join '; ')" }

$imports = [System.Collections.Generic.List[object]]::new()
$byOs = @{}
$physical = $null
foreach ($reportPath in $MachineReportPath) {
    $reportFull = [IO.Path]::GetFullPath($reportPath)
    if (-not (Test-Path -LiteralPath $reportFull -PathType Leaf)) { throw "machine report is missing: $reportFull" }
    $reportResult = Test-QualificationMachineReport -ReportPath $reportFull -PackagePath $PackagePath -ExpectedSourceSha $sourceSha -ExpectedCandidateSha $candidateSha
    if (-not $reportResult.Valid) { throw "machine report failed offline verification '$reportFull': $($reportResult.Failures -join '; ')" }
    $report = $reportResult.Report
    $reportSha = Get-QualificationFileSha256 $reportFull
    $os = Get-QualificationProperty $report 'os'
    $osFamily = [string](Get-QualificationProperty $os 'family')
    if ([string]::IsNullOrWhiteSpace($osFamily)) { $osFamily = [string](Get-QualificationProperty $report 'osFamily') }
    if ($osFamily -notin @('Windows 10', 'Windows 11')) { throw "machine report OS family is unsupported: '$osFamily'" }
    $staged = Stage-QualificationReport -ReportResult $reportResult -ReportPath $reportFull -ArtifactRoot $artifactRoot -ReportSha $reportSha
    if ($byOs.ContainsKey($osFamily)) { throw "more than one machine report was supplied for $osFamily" }
    $byOs[$osFamily] = $staged
    $topology = Get-QualificationProperty $report 'topology'
    $dpis = @(Get-QualificationProperty $topology 'dpiValues') | ForEach-Object { [int]$_ }
    $isPhysical = -not [bool](Get-QualificationProperty $topology 'syntheticTopology') -and
        -not [bool](Get-QualificationProperty $topology 'replayOnly') -and
        [bool](Get-QualificationProperty $topology 'physicalGateEligible') -and
        [int](Get-QualificationProperty $topology 'monitorCount') -ge 2 -and
        [bool](Get-QualificationProperty $topology 'mixedDpi') -and
        @($dpis | Where-Object { $_ -ne 96 }).Count -gt 0
    if ($isPhysical) {
        if ($null -ne $physical) { throw 'more than one physical mixed-DPI machine report was supplied' }
        $physical = $staged
    }
    $imports.Add([ordered]@{
            sourceReportSha256 = $reportSha
            stagedReportRelativePath = $staged.ReportRelativePath
            stagedBundleRelativePath = $staged.BundleRelativePath
            stagedBundleSha256 = $staged.BundleSha256
            sourceCommitSha = $sourceSha
            candidateSha256 = $candidateSha
            osFamily = $osFamily
            nativeAbiResult = 'PASS'
            automatedOutcome = [string](Get-QualificationProperty (Get-QualificationProperty $report 'qualification') 'overall')
        })
}
if ($null -eq $physical) { throw 'no validated physical mixed-DPI machine report was supplied; synthetic or replay evidence cannot be merged as a physical PASS' }
foreach ($requiredOs in @('Windows 10', 'Windows 11')) {
    if (-not $byOs.ContainsKey($requiredOs)) { throw "no validated $requiredOs machine report was supplied" }
}

$physicalReport = $physical.Report
$physicalTopology = Get-QualificationProperty $physicalReport 'topology'
$physicalReportOs = Get-QualificationProperty $physicalReport 'os'
$physicalRecord = [ordered]@{
    status = 'PASS'
    completedAt = [string](Get-QualificationProperty $physicalReport 'createdUtc')
    operator = $Operator
    evidence = 'validated independent-machine report and qualification bundle; see structured hashes'
    qualificationBundleSha256 = $physical.BundleSha256
    runManifestSha256 = $physical.PrimaryRunManifestSha256
    candidateSha256 = $candidateSha
    observedTopology = [ordered]@{
        syntheticTopology = [bool](Get-QualificationProperty $physicalTopology 'syntheticTopology')
        replayOnly = [bool](Get-QualificationProperty $physicalTopology 'replayOnly')
        physicalGateEligible = [bool](Get-QualificationProperty $physicalTopology 'physicalGateEligible')
        monitorCount = [int](Get-QualificationProperty $physicalTopology 'monitorCount')
        mixedDpi = [bool](Get-QualificationProperty $physicalTopology 'mixedDpi')
        dpiValues = @($physicalTopology.dpiValues | ForEach-Object { [int]$_ })
        negativeCoordinates = [bool](Get-QualificationProperty $physicalTopology 'negativeCoordinates')
    }
    machineReport = [ordered]@{
        relativePath = $physical.ReportRelativePath
        reportSha256 = $physical.ReportSha256
        sourceCommitSha = $sourceSha
        candidateSha256 = $candidateSha
        osFamily = [string](Get-QualificationProperty $physicalReportOs 'family')
        architecture = [string](Get-QualificationProperty $physicalReportOs 'architecture')
        nativeAbiResult = 'PASS'
        qualificationBundleSha256 = $physical.BundleSha256
        qualificationBundleRelativePath = $physical.BundleRelativePath
        runManifestSha256 = $physical.PrimaryRunManifestSha256
        verificationStatus = 'PASS'
    }
}
Set-MergeProperty -Object $evidence -Name 'qualificationBundle' -Value ([ordered]@{
        relativePath = $physical.BundleRelativePath
        sha256 = $physical.BundleSha256
        sourceCommitSha = $sourceSha
        candidateSha256 = $candidateSha
        primaryRunManifestSha256 = $physical.PrimaryRunManifestSha256
        syntheticTopology = $false
        replayOnly = $false
        automatedOutcome = [string](Get-QualificationProperty (Get-QualificationProperty $physical.Bundle 'outcome') 'overall')
    })
Set-MergeProperty -Object $evidence -Name 'physicalMixedDpi' -Value ([pscustomobject]$physicalRecord)

$windowsCompatibility = Get-QualificationProperty $evidence 'windowsCompatibility'
if ($null -eq $windowsCompatibility) { $windowsCompatibility = [pscustomobject]@{}; Set-MergeProperty -Object $evidence -Name 'windowsCompatibility' -Value $windowsCompatibility }
foreach ($osPair in @(@('Windows 10', 'windows10'), @('Windows 11', 'windows11'))) {
    $family = [string]$osPair[0]
    $key = [string]$osPair[1]
    $staged = $byOs[$family]
    $report = $staged.Report
    $reportOs = Get-QualificationProperty $report 'os'
    $record = [ordered]@{
        relativePath = $staged.ReportRelativePath
        reportSha256 = $staged.ReportSha256
        sourceCommitSha = $sourceSha
        candidateSha256 = $candidateSha
        osFamily = $family
        architecture = [string](Get-QualificationProperty $reportOs 'architecture')
        nativeAbiResult = 'PASS'
        qualificationBundleSha256 = $staged.BundleSha256
        qualificationBundleRelativePath = $staged.BundleRelativePath
        runManifestSha256 = $staged.PrimaryRunManifestSha256
        verificationStatus = 'PASS'
    }
    $gate = Get-QualificationProperty $windowsCompatibility $key
    if ($null -eq $gate) { $gate = [pscustomobject]@{}; Set-MergeProperty -Object $windowsCompatibility -Name $key -Value $gate }
    Set-MergeProperty -Object $gate -Name 'status' -Value 'PASS'
    Set-MergeProperty -Object $gate -Name 'build' -Value ([string](Get-QualificationProperty $reportOs 'build'))
    Set-MergeProperty -Object $gate -Name 'operator' -Value $Operator
    Set-MergeProperty -Object $gate -Name 'completedAt' -Value ([string](Get-QualificationProperty $report 'createdUtc'))
    Set-MergeProperty -Object $gate -Name 'nativeAbiEvidence' -Value 'structured machine report native ABI result PASS; exact candidate hash bound'
    Set-MergeProperty -Object $gate -Name 'evidence' -Value 'validated independent-machine report and qualification bundle; see structured hashes'
    Set-MergeProperty -Object $gate -Name 'machineReport' -Value $record
}
Set-MergeProperty -Object $windowsCompatibility -Name 'status' -Value 'PASS'
Set-MergeProperty -Object $evidence -Name 'qualificationImports' -Value @($imports)

if ([string]::IsNullOrWhiteSpace($OutputPath)) { $OutputPath = Join-Path $artifactRoot 'release-external-evidence.json' }
$outputFull = [IO.Path]::GetFullPath($OutputPath)
$outputParent = [IO.Path]::GetDirectoryName($outputFull)
if (-not (Test-Path -LiteralPath $outputParent -PathType Container)) { New-Item -ItemType Directory -Path $outputParent -Force | Out-Null }
[IO.File]::WriteAllText($outputFull, ($evidence | ConvertTo-Json -Depth 30), [Text.UTF8Encoding]::new($false))

$finalEvidence = Test-ExternalEvidenceFile -EvidencePath $outputFull -ExpectedSourceSha $sourceSha -ExpectedArtifactSha $candidateSha `
    -ExpectedCandidateRunId ([string](Get-QualificationProperty $evidence 'candidateWorkflowRunId')) `
    -ExpectedCandidateArtifactName ([string](Get-QualificationProperty $evidence 'candidateArtifactName')) `
    -QualificationBundleRoot $artifactRoot -RequireQualificationBundle
if (-not $finalEvidence.Valid) { throw "merged external evidence failed offline publication verification: $($finalEvidence.Failures -join '; ')" }

if (-not [string]::IsNullOrWhiteSpace($HandoffDir)) {
    $handoffRoot = [IO.Path]::GetFullPath($HandoffDir)
    $artifactPrefix = $artifactRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (([string]::Equals($handoffRoot, $artifactRoot, [StringComparison]::OrdinalIgnoreCase)) -or
        ($handoffRoot.StartsWith($artifactPrefix, [StringComparison]::OrdinalIgnoreCase))) {
        throw 'qualification evidence handoff must be outside the retained candidate artifact directory'
    }
    if (Test-Path -LiteralPath $handoffRoot) {
        if (@(Get-ChildItem -LiteralPath $handoffRoot -Force).Count -gt 0) { throw "qualification evidence handoff directory is not empty: $handoffRoot" }
    }
    else { New-Item -ItemType Directory -Path $handoffRoot -Force | Out-Null }

    # The publication handoff is deliberately bounded to the merged evidence
    # record and the externally staged bundle trees. It does not include the
    # retained candidate root, release manifest, checksums, scripts, or source.
    Copy-Item -LiteralPath $outputFull -Destination (Join-Path $handoffRoot 'release-external-evidence.json')
    foreach ($staged in @($imports)) {
        $stageRoot = [IO.Path]::GetDirectoryName([string]$staged.stagedReportRelativePath)
        if ([string]::IsNullOrWhiteSpace($stageRoot)) { throw 'staged machine report has no bounded qualification directory' }
        $sourceStageRoot = Join-Path $artifactRoot ($stageRoot -replace '/', '\')
        if (-not (Test-Path -LiteralPath $sourceStageRoot -PathType Container)) { throw "staged qualification directory is missing: $stageRoot" }
        foreach ($file in @(Get-ChildItem -LiteralPath $sourceStageRoot -Recurse -File)) {
            $relative = (Resolve-QualificationRelativePath -Root $artifactRoot -RelativePath ([IO.Path]::GetRelativePath($artifactRoot, $file.FullName))).RelativePath
            if ($relative -notlike 'qualification/external/*') { throw "staged handoff path is outside qualification/external: '$relative'" }
            $destination = Join-Path $handoffRoot ($relative -replace '/', '\')
            $destinationParent = [IO.Path]::GetDirectoryName($destination)
            if (-not (Test-Path -LiteralPath $destinationParent -PathType Container)) { New-Item -ItemType Directory -Path $destinationParent -Force | Out-Null }
            Copy-ValidatedQualificationFile -SourcePath $file.FullName -DestinationPath $destination
        }
    }
    Write-Host "Qualification evidence handoff: $handoffRoot (upload this data-only directory for Stage B)" -ForegroundColor Green
}

Write-Host "Merged external evidence: $outputFull" -ForegroundColor Green
Write-Host "Candidate SHA-256: $candidateSha" -ForegroundColor Green
Write-Host "Physical qualification bundle SHA-256: $($physical.BundleSha256)" -ForegroundColor Green
