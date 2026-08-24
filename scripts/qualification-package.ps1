<#
.SYNOPSIS
    Shared verifier for bounded independent-machine qualification packages.

.DESCRIPTION
    A package is untrusted data at import time. The verifier accepts only the
    declared, portable file set, re-hashes every byte, cross-checks the release
    manifest and candidate executable, and validates returned machine reports
    without launching any executable or script.
#>

function Get-QualificationPackageSchemaVersion {
    return 1
}

function Get-QualificationPackageRoot {
    param([Parameter(Mandatory = $true)][string]$PackagePath)
    $full = [IO.Path]::GetFullPath($PackagePath)
    if (Test-Path -LiteralPath $full -PathType Container) { return $full }
    if (Test-Path -LiteralPath $full -PathType Leaf) { return [IO.Path]::GetDirectoryName($full) }
    throw "qualification package path is missing: $PackagePath"
}

function Get-QualificationPackageManifestPath {
    param([Parameter(Mandatory = $true)][string]$PackagePath)
    $root = Get-QualificationPackageRoot $PackagePath
    $manifest = Join-Path $root 'qualification-package.json'
    if (-not (Test-Path -LiteralPath $manifest -PathType Leaf)) {
        throw "qualification-package.json is missing from '$root'"
    }
    return $manifest
}

function Test-QualificationPackage {
    param(
        [Parameter(Mandatory = $true)][string]$PackagePath,
        [string]$ExpectedSourceSha = '',
        [string]$ExpectedCandidateSha = ''
    )
    $failures = [System.Collections.Generic.List[string]]::new()
    try {
        $root = Get-QualificationPackageRoot $PackagePath
        $manifestPath = Get-QualificationPackageManifestPath $PackagePath
        $json = Read-QualificationJsonFile $manifestPath
    }
    catch {
        [void]$failures.Add($_.Exception.Message)
        return [pscustomobject]@{ Valid = $false; Failures = @($failures); Root = $null; Package = $null; ArtifactMap = @{} }
    }
    foreach ($failure in @($json.DuplicateFailures)) { [void]$failures.Add($failure) }
    $package = $json.Value
    if ([int](Get-QualificationProperty $package 'schemaVersion') -ne (Get-QualificationPackageSchemaVersion)) {
        [void]$failures.Add('qualification package schemaVersion is unsupported')
    }
    if ([string](Get-QualificationProperty $package 'packageKind') -ne 'qualification-handoff') {
        [void]$failures.Add('qualification package packageKind is not qualification-handoff')
    }
    $createdUtc = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse([string](Get-QualificationProperty $package 'createdUtc'), [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind, [ref]$createdUtc)) {
        [void]$failures.Add('qualification package createdUtc is malformed')
    }
    elseif ($createdUtc -gt [DateTimeOffset]::UtcNow.AddMinutes(5)) {
        [void]$failures.Add('qualification package createdUtc is materially in the future')
    }
    $sourceSha = [string](Get-QualificationProperty $package 'sourceCommitSha')
    if ($sourceSha -notmatch '^[0-9a-fA-F]{40}$') { [void]$failures.Add('qualification package sourceCommitSha is malformed') }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedSourceSha) -and
        -not [string]::Equals($sourceSha, $ExpectedSourceSha, [StringComparison]::OrdinalIgnoreCase)) {
        [void]$failures.Add('qualification package sourceCommitSha disagrees with the expected source')
    }
    $artifactMap = @{}
    foreach ($entry in @(Get-QualificationProperty $package 'artifactIndex')) {
        try { $resolved = Resolve-QualificationRelativePath -Root $root -RelativePath ([string](Get-QualificationProperty $entry 'relativePath')) }
        catch { [void]$failures.Add($_.Exception.Message); continue }
        $relative = $resolved.RelativePath
        if ($relative -eq 'qualification-package.json') {
            [void]$failures.Add('qualification package artifactIndex must not self-index qualification-package.json')
        }
        if ($artifactMap.ContainsKey($relative)) {
            [void]$failures.Add("qualification package artifactIndex duplicates '$relative'")
            continue
        }
        $hash = [string](Get-QualificationProperty $entry 'sha256')
        if ($hash -notmatch '^[0-9a-fA-F]{64}$') { [void]$failures.Add("qualification package artifact '$relative' has a malformed SHA-256") }
        if (-not (Test-Path -LiteralPath $resolved.FullPath -PathType Leaf)) {
            [void]$failures.Add("qualification package artifact is missing: '$relative'")
        }
        else {
            $actual = Get-QualificationFileSha256 $resolved.FullPath
            if (-not [string]::Equals($actual, $hash, [StringComparison]::OrdinalIgnoreCase)) {
                [void]$failures.Add("qualification package artifact hash mismatch: '$relative'")
            }
        }
        $artifactMap[$relative] = [pscustomobject]@{ FullPath = $resolved.FullPath; Hash = $hash; Entry = $entry }
    }
    $declaredFiles = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($relative in $artifactMap.Keys) { [void]$declaredFiles.Add($relative) }
    foreach ($file in @(Get-ChildItem -LiteralPath $root -Recurse -File)) {
        $relative = (Resolve-QualificationRelativePath -Root $root -RelativePath ([IO.Path]::GetRelativePath($root, $file.FullName))).RelativePath
        if ($relative -eq 'qualification-package.json') { continue }
        if (-not $declaredFiles.Contains($relative)) {
            [void]$failures.Add("qualification package contains an unindexed file: '$relative'")
        }
    }

    $candidate = Get-QualificationProperty $package 'candidate'
    $candidateRelative = $null
    try { $candidateRelative = (Resolve-QualificationRelativePath -Root $root -RelativePath ([string](Get-QualificationProperty $candidate 'relativePath'))).RelativePath }
    catch { [void]$failures.Add($_.Exception.Message) }
    $candidateSha = [string](Get-QualificationProperty $candidate 'sha256')
    if ($candidateSha -notmatch '^[0-9a-fA-F]{64}$') { [void]$failures.Add('qualification package candidate SHA-256 is malformed') }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedCandidateSha) -and
        -not [string]::Equals($candidateSha, $ExpectedCandidateSha, [StringComparison]::OrdinalIgnoreCase)) {
        [void]$failures.Add('qualification package candidate SHA-256 disagrees with the expected candidate')
    }
    if ($null -ne $candidateRelative -and -not $artifactMap.ContainsKey($candidateRelative)) {
        [void]$failures.Add("qualification package candidate '$candidateRelative' is not indexed")
    }
    elseif ($null -ne $candidateRelative -and -not [string]::Equals($artifactMap[$candidateRelative].Hash, $candidateSha, [StringComparison]::OrdinalIgnoreCase)) {
        [void]$failures.Add('qualification package candidate index hash disagrees')
    }

    $releaseRelative = $null
    try { $releaseRelative = (Resolve-QualificationRelativePath -Root $root -RelativePath ([string](Get-QualificationProperty $candidate 'releaseManifestRelativePath'))).RelativePath }
    catch { [void]$failures.Add($_.Exception.Message) }
    $releaseSha = [string](Get-QualificationProperty $candidate 'releaseManifestSha256')
    if ($null -ne $releaseRelative -and -not $artifactMap.ContainsKey($releaseRelative)) {
        [void]$failures.Add("qualification package release manifest '$releaseRelative' is not indexed")
    }
    elseif ($null -ne $releaseRelative) {
        if (-not [string]::Equals($artifactMap[$releaseRelative].Hash, $releaseSha, [StringComparison]::OrdinalIgnoreCase)) {
            [void]$failures.Add('qualification package release manifest hash disagrees')
        }
        try {
            $releaseJson = Read-QualificationJsonFile $artifactMap[$releaseRelative].FullPath
            foreach ($failure in @($releaseJson.DuplicateFailures)) { [void]$failures.Add($failure) }
            $release = $releaseJson.Value
            if (-not [string]::Equals([string](Get-QualificationProperty $release 'sourceCommitSha'), $sourceSha, [StringComparison]::OrdinalIgnoreCase)) { [void]$failures.Add('package release manifest sourceCommitSha disagrees') }
            if (-not [string]::Equals([string](Get-QualificationProperty $release 'artifactSha256'), $candidateSha, [StringComparison]::OrdinalIgnoreCase)) { [void]$failures.Add('package release manifest artifactSha256 disagrees') }
        }
        catch { [void]$failures.Add("package release manifest is not strict JSON: $($_.Exception.Message)") }
    }

    $driver = Get-QualificationProperty $package 'driver'
    $driverRelative = $null
    try { $driverRelative = (Resolve-QualificationRelativePath -Root $root -RelativePath ([string](Get-QualificationProperty $driver 'relativePath'))).RelativePath }
    catch { [void]$failures.Add($_.Exception.Message) }
    $driverSha = [string](Get-QualificationProperty $driver 'sha256')
    if ($driverSha -notmatch '^[0-9a-fA-F]{64}$') { [void]$failures.Add('qualification package driver SHA-256 is malformed') }
    if ($null -ne $driverRelative -and -not $artifactMap.ContainsKey($driverRelative)) { [void]$failures.Add("qualification package driver '$driverRelative' is not indexed") }
    elseif ($null -ne $driverRelative -and -not [string]::Equals($artifactMap[$driverRelative].Hash, $driverSha, [StringComparison]::OrdinalIgnoreCase)) { [void]$failures.Add('qualification package driver hash disagrees') }
    foreach ($runtimeEntry in @(Get-QualificationProperty $driver 'runtimeFiles')) {
        try {
            $runtimeRelative = (Resolve-QualificationRelativePath -Root $root -RelativePath ([string](Get-QualificationProperty $runtimeEntry 'relativePath'))).RelativePath
            if (-not $artifactMap.ContainsKey($runtimeRelative)) { [void]$failures.Add("qualification package driver runtime file '$runtimeRelative' is not indexed") }
            elseif (-not [string]::Equals($artifactMap[$runtimeRelative].Hash, [string](Get-QualificationProperty $runtimeEntry 'sha256'), [StringComparison]::OrdinalIgnoreCase)) { [void]$failures.Add("qualification package driver runtime file hash disagrees: '$runtimeRelative'") }
        }
        catch { [void]$failures.Add("qualification package driver runtime file is invalid: $($_.Exception.Message)") }
    }

    $guineaPig = Get-QualificationProperty $package 'guineaPig'
    if ($null -ne $guineaPig) {
        $pigRelative = $null
        try { $pigRelative = (Resolve-QualificationRelativePath -Root $root -RelativePath ([string](Get-QualificationProperty $guineaPig 'relativePath'))).RelativePath }
        catch { [void]$failures.Add($_.Exception.Message) }
        $pigSha = [string](Get-QualificationProperty $guineaPig 'sha256')
        if ($pigSha -notmatch '^[0-9a-fA-F]{64}$') { [void]$failures.Add('qualification package GuineaPig SHA-256 is malformed') }
        if ($null -ne $pigRelative -and -not $artifactMap.ContainsKey($pigRelative)) { [void]$failures.Add("qualification package GuineaPig '$pigRelative' is not indexed") }
        elseif ($null -ne $pigRelative -and -not [string]::Equals($artifactMap[$pigRelative].Hash, $pigSha, [StringComparison]::OrdinalIgnoreCase)) { [void]$failures.Add('qualification package GuineaPig hash disagrees') }
        foreach ($runtimeEntry in @(Get-QualificationProperty $guineaPig 'runtimeFiles')) {
            try {
                $runtimeRelative = (Resolve-QualificationRelativePath -Root $root -RelativePath ([string](Get-QualificationProperty $runtimeEntry 'relativePath'))).RelativePath
                if (-not $artifactMap.ContainsKey($runtimeRelative)) { [void]$failures.Add("qualification package GuineaPig runtime file '$runtimeRelative' is not indexed") }
                elseif (-not [string]::Equals($artifactMap[$runtimeRelative].Hash, [string](Get-QualificationProperty $runtimeEntry 'sha256'), [StringComparison]::OrdinalIgnoreCase)) { [void]$failures.Add("qualification package GuineaPig runtime file hash disagrees: '$runtimeRelative'") }
            }
            catch { [void]$failures.Add("qualification package GuineaPig runtime file is invalid: $($_.Exception.Message)") }
        }
    }

    $privacy = Get-QualificationProperty $package 'privacy'
    if (([bool](Get-QualificationProperty $privacy 'privacySafe') -ne $true) -or
        ([bool](Get-QualificationProperty $privacy 'containsRawDesktopData') -ne $false)) {
        [void]$failures.Add('qualification package privacy contract is not explicitly safe')
    }
    $privacyFailures = [System.Collections.Generic.List[string]]::new()
    Test-QualificationPrivacyObject -Value $package -Path 'qualification-package.json' -Failures $privacyFailures
    foreach ($failure in @($privacyFailures)) { [void]$failures.Add($failure) }

    return [pscustomobject]@{
        Valid = $failures.Count -eq 0
        Failures = @($failures)
        Root = $root
        Package = $package
        ArtifactMap = $artifactMap
        CandidateRelativePath = $candidateRelative
        CandidateSha256 = $candidateSha
        DriverSha256 = $driverSha
    }
}

function Test-QualificationMachineReport {
    param(
        [Parameter(Mandatory = $true)][string]$ReportPath,
        [Parameter(Mandatory = $true)][string]$PackagePath,
        [string]$ExpectedSourceSha = '',
        [string]$ExpectedCandidateSha = '',
        [switch]$RequirePhysicalMixedDpi
    )
    $failures = [System.Collections.Generic.List[string]]::new()
    $packageResult = Test-QualificationPackage -PackagePath $PackagePath -ExpectedSourceSha $ExpectedSourceSha -ExpectedCandidateSha $ExpectedCandidateSha
    foreach ($failure in @($packageResult.Failures)) { [void]$failures.Add([string]$failure) }
    $report = $null
    $reportRoot = $null
    try {
        $reportFull = [IO.Path]::GetFullPath($ReportPath)
        $reportRoot = [IO.Path]::GetDirectoryName($reportFull)
        $json = Read-QualificationJsonFile $reportFull
        foreach ($failure in @($json.DuplicateFailures)) { [void]$failures.Add($failure) }
        $report = $json.Value
    }
    catch { [void]$failures.Add($_.Exception.Message) }
    if ($null -eq $report) { return [pscustomobject]@{ Valid = $false; Failures = @($failures); Report = $null; Package = $packageResult } }
    if ([int](Get-QualificationProperty $report 'schemaVersion') -ne 1) { [void]$failures.Add('machine report schemaVersion is unsupported') }
    if ([string](Get-QualificationProperty $report 'reportKind') -ne 'independent-machine-qualification') { [void]$failures.Add('machine report reportKind is unsupported') }
    $reportCreatedUtc = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse([string](Get-QualificationProperty $report 'createdUtc'), [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind, [ref]$reportCreatedUtc)) {
        [void]$failures.Add('machine report createdUtc is malformed')
    }
    elseif ($reportCreatedUtc -gt [DateTimeOffset]::UtcNow.AddMinutes(5)) {
        [void]$failures.Add('machine report createdUtc is materially in the future')
    }
    $sourceSha = [string](Get-QualificationProperty $report 'sourceCommitSha')
    $candidateSha = [string](Get-QualificationProperty $report 'candidateSha256')
    if (-not [string]::Equals($sourceSha, [string](Get-QualificationProperty $packageResult.Package 'sourceCommitSha'), [StringComparison]::OrdinalIgnoreCase)) { [void]$failures.Add('machine report sourceCommitSha disagrees with package') }
    if (-not [string]::Equals($candidateSha, $packageResult.CandidateSha256, [StringComparison]::OrdinalIgnoreCase)) { [void]$failures.Add('machine report candidateSha256 disagrees with package') }
    $nativeAbi = Get-QualificationProperty $report 'nativeAbi'
    if ([string](Get-QualificationProperty $nativeAbi 'status') -ne 'PASS') { [void]$failures.Add('machine report native ABI result is not PASS') }
    if (-not [string]::Equals([string](Get-QualificationProperty $nativeAbi 'candidateSha256'), $candidateSha, [StringComparison]::OrdinalIgnoreCase)) { [void]$failures.Add('machine report native ABI candidate hash disagrees') }
    $os = Get-QualificationProperty $report 'os'
    $osFamily = [string](Get-QualificationProperty $os 'family')
    if ($osFamily -notin @('Windows 10', 'Windows 11')) { [void]$failures.Add("machine report OS family '$osFamily' is not supported") }
    if ([string](Get-QualificationProperty $os 'architecture') -notin @('X64', 'AMD64')) { [void]$failures.Add('machine report architecture is not x64') }
    $topology = Get-QualificationProperty $report 'topology'
    $synthetic = [bool](Get-QualificationProperty $topology 'syntheticTopology')
    $replay = [bool](Get-QualificationProperty $topology 'replayOnly')
    if ($RequirePhysicalMixedDpi) {
        if ($synthetic -or $replay) { [void]$failures.Add('synthetic or replay topology cannot satisfy physical mixed-DPI import') }
        if ([bool](Get-QualificationProperty $topology 'physicalGateEligible') -ne $true) { [void]$failures.Add('machine report does not assert physicalGateEligible') }
        if ([int](Get-QualificationProperty $topology 'monitorCount') -lt 2) { [void]$failures.Add('physical mixed-DPI report observed fewer than two monitors') }
        if ([bool](Get-QualificationProperty $topology 'mixedDpi') -ne $true) { [void]$failures.Add('physical mixed-DPI report did not observe mixed DPI') }
        $dpis = @(Get-QualificationProperty $topology 'dpiValues') | ForEach-Object { [int]$_ }
        $nonDefaultDpiCount = @($dpis | Where-Object { $_ -ne 96 }).Count
        if ($dpis.Count -lt 2 -or $nonDefaultDpiCount -eq 0) { [void]$failures.Add('physical mixed-DPI report has no distinct non-default DPI values') }
    }
    $qualification = Get-QualificationProperty $report 'qualification'
    $overall = [string](Get-QualificationProperty $qualification 'overall')
    if ($overall -ne 'PASS') { [void]$failures.Add("machine qualification overall outcome is '$overall'") }
    $counts = Get-QualificationProperty $qualification 'scenarioCounts'
    foreach ($code in @('FAIL_PRODUCT', 'FAIL_HARNESS', 'BLOCKED_ENVIRONMENT', 'BLOCKED_SUPERVISED', 'BLOCKED_CAPABILITY', 'SKIP_CAPABILITY', 'FLAKE_UNCLASSIFIED')) {
        $value = Get-QualificationProperty $counts $code
        if ($null -ne $value -and [int]$value -gt 0) { [void]$failures.Add("machine qualification contains non-PASS outcome '$code'") }
    }
    $reportedScenarioCount = Get-QualificationProperty $qualification 'scenarioCount'
    $reportedAttemptCount = Get-QualificationProperty $qualification 'attemptCount'
    if ($null -eq $reportedScenarioCount -or $null -eq $reportedAttemptCount) {
        [void]$failures.Add('machine qualification is missing scenarioCount or attemptCount')
    }
    $reportedDriverSha = [string](Get-QualificationProperty $report 'driverSha256')
    if (-not [string]::Equals($reportedDriverSha, $packageResult.DriverSha256, [StringComparison]::OrdinalIgnoreCase)) { [void]$failures.Add('machine report driverSha256 disagrees with package') }
    $reportedPackageSha = [string](Get-QualificationProperty $report 'packageSha256')
    if (-not [string]::Equals($reportedPackageSha, (Get-QualificationFileSha256 (Get-QualificationPackageManifestPath $PackagePath)), [StringComparison]::OrdinalIgnoreCase)) { [void]$failures.Add('machine report packageSha256 disagrees with package manifest') }
    $reportRunEntries = @(Get-QualificationProperty $report 'runManifestHashes')
    if ($reportRunEntries.Count -eq 0) { [void]$failures.Add('machine report runManifestHashes is empty') }
    $reportRunMap = @{}
    foreach ($runEntry in $reportRunEntries) {
        try {
            $runRelative = (Resolve-QualificationRelativePath -Root $reportRoot -RelativePath ([string](Get-QualificationProperty $runEntry 'relativePath'))).RelativePath
            if ($reportRunMap.ContainsKey($runRelative)) { [void]$failures.Add("machine report run manifest is duplicated: $runRelative") }
            $reportRunMap[$runRelative] = [string](Get-QualificationProperty $runEntry 'sha256')
            $runPath = (Resolve-QualificationRelativePath -Root $reportRoot -RelativePath $runRelative).FullPath
            $runHash = Get-QualificationFileSha256 $runPath
            if (-not [string]::Equals($runHash, $reportRunMap[$runRelative], [StringComparison]::OrdinalIgnoreCase)) { [void]$failures.Add("machine report run manifest hash disagrees: $runRelative") }
        }
        catch { [void]$failures.Add("machine report run manifest cannot be verified: $($_.Exception.Message)") }
    }
    $reportPrivacy = Get-QualificationProperty $report 'privacy'
    if ([bool](Get-QualificationProperty $reportPrivacy 'privacySafe') -ne $true -or [bool](Get-QualificationProperty $reportPrivacy 'containsRawDesktopData') -ne $false) { [void]$failures.Add('machine report privacy contract is not explicitly safe') }
    $bundleRelative = [string](Get-QualificationProperty $report 'qualificationBundleRelativePath')
    if ([string]::IsNullOrWhiteSpace($bundleRelative)) { [void]$failures.Add('machine report qualificationBundleRelativePath is missing') }
    $reportedBundleSha = [string](Get-QualificationProperty $report 'qualificationBundleSha256')
    if ($reportedBundleSha -notmatch '^[0-9a-fA-F]{64}$') { [void]$failures.Add('machine report qualificationBundleSha256 is malformed') }
    try {
        $bundlePath = (Resolve-QualificationRelativePath -Root $reportRoot -RelativePath $bundleRelative).FullPath
        $bundleHash = Get-QualificationFileSha256 $bundlePath
        if (-not [string]::Equals($bundleHash, $reportedBundleSha, [StringComparison]::OrdinalIgnoreCase)) { [void]$failures.Add('machine report qualification bundle hash disagrees') }
        $bundleResult = Test-QualificationBundle -BundlePath $bundlePath -ExpectedSourceSha $sourceSha -ExpectedArtifactSha $candidateSha -RequirePhysicalTopology:$RequirePhysicalMixedDpi
        foreach ($failure in @($bundleResult.Failures)) { [void]$failures.Add([string]$failure) }
        if ($null -ne $bundleResult.Bundle) {
            $bundleOutcome = Get-QualificationProperty $bundleResult.Bundle 'outcome'
            if (-not [string]::Equals($overall, [string](Get-QualificationProperty $bundleOutcome 'overall'), [StringComparison]::Ordinal)) { [void]$failures.Add('machine qualification overall disagrees with qualification bundle') }
            if ($null -ne $reportedScenarioCount -and [int]$reportedScenarioCount -ne [int](Get-QualificationProperty $bundleOutcome 'scenarioCount')) { [void]$failures.Add('machine qualification scenarioCount disagrees with qualification bundle') }
            if ($null -ne $reportedAttemptCount -and [int]$reportedAttemptCount -ne [int](Get-QualificationProperty $bundleOutcome 'attemptCount')) { [void]$failures.Add('machine qualification attemptCount disagrees with qualification bundle') }
            $bundleCounts = Get-QualificationProperty $bundleOutcome 'scenarioCounts'
            foreach ($code in (Get-QualificationOutcomeCodes)) {
                $reported = Get-QualificationProperty $counts $code
                $declared = Get-QualificationProperty $bundleCounts $code
                if ($null -eq $reported) { $reported = 0 }
                if ($null -eq $declared) { $declared = 0 }
                if ([int]$reported -ne [int]$declared) { [void]$failures.Add("machine qualification scenarioCounts.$code disagrees with qualification bundle") }
            }
            $primaryRelative = [string](Get-QualificationProperty $bundleResult.Bundle 'primaryRunManifest')
            $primaryEntry = @((Get-QualificationProperty $bundleResult.Bundle 'runManifests') | Where-Object { [string](Get-QualificationProperty $_ 'relativePath') -eq $primaryRelative }) | Select-Object -First 1
            $reportedPrimarySha = [string](Get-QualificationProperty (Get-QualificationProperty $report 'evidenceHashes') 'primaryRunManifestSha256')
            if ($null -ne $primaryEntry -and -not [string]::Equals($reportedPrimarySha, [string](Get-QualificationProperty $primaryEntry 'sha256'), [StringComparison]::OrdinalIgnoreCase)) { [void]$failures.Add('machine report primary run manifest hash disagrees with qualification bundle') }
            if ($null -ne $primaryEntry -and -not $reportRunMap.ContainsKey($primaryRelative)) { [void]$failures.Add('machine report runManifestHashes does not include the bundle primary run manifest') }
        }
    }
    catch { [void]$failures.Add("machine report qualification bundle cannot be verified: $($_.Exception.Message)") }
    $privacyFailures = [System.Collections.Generic.List[string]]::new()
    Test-QualificationPrivacyObject -Value $report -Path 'machine-report.json' -Failures $privacyFailures
    foreach ($failure in @($privacyFailures)) { [void]$failures.Add($failure) }
    return [pscustomobject]@{ Valid = $failures.Count -eq 0; Failures = @($failures); Report = $report; Package = $packageResult }
}
