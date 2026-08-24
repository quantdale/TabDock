<#
.SYNOPSIS
    Shared release-tooling function library for TabDock (dot-source only).

.DESCRIPTION
    Pure function library (no side effects at import) shared by
    scripts/release-qualify.ps1, scripts/sign-release.ps1, the release
    workflow's publication job, and scripts/release-tooling-tests.ps1.

    Dot-source it:  . (Join-Path $PSScriptRoot 'release-tooling.ps1')

    Owns the release-record semantics:
      - final distributed hash selection (signed vs unsigned)
      - SHA256SUMS.txt generation, parsing, and verification
      - release-manifest.json / SHA256SUMS.txt / on-disk triple consistency
      - external production evidence (release-external-evidence.json)
        validation and the fail-closed publication eligibility gate
    and the signing-provider policy:
      - SIGNING_PROVIDER vocabulary and classification
        (not-configured / local-pfx / digicert-stm / mock-test)
      - the approved production provider allowlist (non-exportable
        HSM/cloud private keys only) and the fail-closed provider
        configuration preflight (names missing variables, never values)
    plus the signtool discovery, Authenticode verification, RFC3161
    timestamp verification, and signed-certificate identity helpers shared
    with scripts/sign-release.ps1.

    Trust boundary: this module IS the current trusted release policy. Stage B
    loads it exclusively from the trusted policy checkout
    (policy/scripts/release-tooling.ps1) of the workflow revision being
    executed - NEVER from the candidate source, the candidate artifact, or the
    candidate manifest. The publication verdict can therefore never be supplied
    by the candidate being evaluated. The release-policy schema contract
    (Get-ReleasePolicySchemaVersion / Get-MinimumAcceptedProductionPolicySchema)
    additionally rejects candidates produced under older policy generations.

    Nothing in this file reads signing material values, performs signing, or
    creates a GitHub Release; it is safe to run anywhere, including tests.

.NOTES
    Hash vocabulary (see docs/release/publication-gates.md):
      unsignedQualifiedSha256 = hash of the exact executable that passed
                                pre-sign qualification
      finalSignedSha256       = hash after Authenticode signing + verification
      artifactSha256          = hash of the FINAL DISTRIBUTED TabDock.exe
      SHA256SUMS.txt          = hash of the FINAL DISTRIBUTED TabDock.exe
    For an unsigned qualification the final hash equals
    unsignedQualifiedSha256 and finalSignedSha256 is absent; for a signed
    qualification the final hash equals finalSignedSha256 and
    unsignedQualifiedSha256 is retained as provenance.
#>

function Get-FileSha256Lower {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "File not found for hashing: $Path"
    }
    return (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant()
}

function Get-FinalArtifactSha256 {
    <#
    .SYNOPSIS
        The hash of the FINAL DISTRIBUTED artifact as recorded by a release
        manifest: finalSignedSha256 when the artifact was signed, otherwise
        unsignedQualifiedSha256. Throws when neither is present.
    #>
    param([Parameter(Mandatory = $true)]$Manifest)
    $final = [string]$Manifest.finalSignedSha256
    if ([string]::IsNullOrWhiteSpace($final)) {
        $final = [string]$Manifest.unsignedQualifiedSha256
    }
    if ([string]::IsNullOrWhiteSpace($final)) {
        throw 'Release manifest records neither finalSignedSha256 nor unsignedQualifiedSha256'
    }
    return $final.ToLowerInvariant()
}

function Write-Sha256Sums {
    <#
    .SYNOPSIS
        Writes SHA256SUMS.txt ("<sha256>  <filename>"). The hash must be the
        FINAL DISTRIBUTED artifact hash.
    #>
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Hash,
        [Parameter(Mandatory = $true)][string]$FileName
    )
    if ($Hash -notmatch '^[0-9a-f]{64}$') {
        throw "Refusing to write SHA256SUMS.txt with a malformed hash: '$Hash'"
    }
    [IO.File]::WriteAllText($Path, "$Hash  $FileName`r`n", [Text.UTF8Encoding]::new($false))
}

function Read-Sha256Sums {
    <#
    .SYNOPSIS
        Parses SHA256SUMS.txt and returns [pscustomobject]@{ Hash; FileName }.
        Throws when the file is missing or contains no valid entry.
    #>
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "SHA256SUMS.txt is missing: $Path"
    }
    $text = [IO.File]::ReadAllText($Path)
    $match = [regex]::Match($text, '(?im)^[ \t]*([0-9a-fA-F]{64})[ \t]+(\S+)[ \t]*\r?$')
    if (-not $match.Success) {
        throw "SHA256SUMS.txt is malformed (no '<sha256>  <filename>' entry): $Path"
    }
    return [pscustomobject]@{
        Hash     = $match.Groups[1].Value.ToLowerInvariant()
        FileName = $match.Groups[2].Value
    }
}

function Assert-ChecksumsMatchArtifact {
    <#
    .SYNOPSIS
        Proves the actual bytes of the artifact match SHA256SUMS.txt. Returns
        the actual hash; throws on any mismatch.
    #>
    param(
        [Parameter(Mandatory = $true)][string]$ArtifactPath,
        [Parameter(Mandatory = $true)][string]$SumsPath
    )
    $actual = Get-FileSha256Lower $ArtifactPath
    $sums = Read-Sha256Sums $SumsPath
    if ($actual -ne $sums.Hash) {
        throw "SHA256SUMS mismatch: actual artifact SHA-256 $actual != SHA256SUMS.txt $($sums.Hash)"
    }
    return $actual
}

function Get-RequiredReleaseAssets {
    <#
    .SYNOPSIS
        The exact set of release assets every production publication must
        upload. The Stage B publish job gates on this list BEFORE
        `gh release create` (fail closed), and the post-publication
        verification re-checks it against the created release.
    #>
    return @(
        'TabDock.exe'
        'SHA256SUMS.txt'
        'release-manifest.json'
        'release-external-evidence.json'
        'publication-verification.json'
    )
}

function Assert-ReleaseAssetsPresent {
    <#
    .SYNOPSIS
        Fail-closed pre-publication gate: every required release asset must
        PHYSICALLY EXIST in the publish workspace before `gh release create`
        runs. Throws naming the first missing asset.

    .DESCRIPTION
        R21-002: the validated external evidence travels IN the verified
        same-run handoff (bound to publication-verification.json); the
        candidate artifact directory supplies the exact Stage A bytes and
        their manifest/checksums. A missing file aborts the release mutation
        instead of producing a release with missing assets.
    #>
    param(
        [Parameter(Mandatory = $true)][string]$CandidateArtifactDir,
        [Parameter(Mandatory = $true)][string]$VerifiedHandoffDir
    )
    $locations = @{
        'TabDock.exe'                    = (Join-Path $CandidateArtifactDir 'TabDock.exe')
        'SHA256SUMS.txt'                 = (Join-Path $CandidateArtifactDir 'SHA256SUMS.txt')
        'release-manifest.json'          = (Join-Path $CandidateArtifactDir 'release-manifest.json')
        'release-external-evidence.json' = (Join-Path $VerifiedHandoffDir 'release-external-evidence.json')
        'publication-verification.json'  = (Join-Path $VerifiedHandoffDir 'publication-verification.json')
    }
    foreach ($name in (Get-RequiredReleaseAssets)) {
        $path = $locations[$name]
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "required release asset is MISSING from the publish workspace: $name (expected at $path); refusing to create the release"
        }
    }
}

function Complete-ReleaseRecords {
    <#
    .SYNOPSIS
        Finalizes release records for the FINAL distributed artifact: sets
        manifest.artifactSha256 to the on-disk hash (finalSignedSha256 when
        signed, else unsignedQualifiedSha256), writes release-manifest.json
        and SHA256SUMS.txt, and proves file == manifest == SHA256SUMS.txt.

    .DESCRIPTION
        This is the checksum-ordering invariant that previously made
        SHA256SUMS.txt describe the UNSIGNED executable when signing changed
        the bytes: the manifest and checksum file are only ever written from
        the hash of the artifact AS IT EXISTS at finalization time, and the
        final hash is cross-checked against the record fields:
          - signed: on-disk hash must equal finalSignedSha256
          - unsigned: on-disk hash must equal unsignedQualifiedSha256
        Any disagreement throws (fail closed) before a manifest/checksum pair
        can describe the wrong bytes.
    #>
    param(
        [Parameter(Mandatory = $true)][System.Collections.IDictionary]$Manifest,
        [Parameter(Mandatory = $true)][string]$ArtifactPath,
        [Parameter(Mandatory = $true)][string]$OutDir
    )
    $fileName = [string]$Manifest.artifactFileName
    if ([string]::IsNullOrWhiteSpace($fileName)) {
        $fileName = 'TabDock.exe'
    }
    $actual = Get-FileSha256Lower $ArtifactPath
    $finalSigned = [string]$Manifest.finalSignedSha256
    $unsignedQualified = [string]$Manifest.unsignedQualifiedSha256

    if (-not [string]::IsNullOrWhiteSpace($finalSigned)) {
        # Signed path: the file must be exactly what sign-release.ps1 reported
        # after Authenticode signing and verification.
        if ($actual -ne $finalSigned.ToLowerInvariant()) {
            throw "finalSignedSha256 $finalSigned does not match the artifact on disk ($actual)"
        }
        $Manifest.artifactSha256 = $actual
    }
    else {
        # Unsigned path: the artifact must still be the qualified bytes.
        if (-not [string]::IsNullOrWhiteSpace($unsignedQualified) -and
            $actual -ne $unsignedQualified.ToLowerInvariant()) {
            throw "Artifact changed after qualification: unsignedQualifiedSha256 $unsignedQualified != on-disk SHA-256 $actual"
        }
        $Manifest.artifactSha256 = $actual
    }

    $manifestJson = $Manifest | ConvertTo-Json -Depth 6
    $manifestPath = Join-Path $OutDir 'release-manifest.json'
    [IO.File]::WriteAllText($manifestPath, $manifestJson, [Text.UTF8Encoding]::new($false))

    $sumsPath = Join-Path $OutDir 'SHA256SUMS.txt'
    Write-Sha256Sums $sumsPath $actual $fileName

    # Triple consistency: actual file vs manifest vs SHA256SUMS.txt.
    $fromSums = Assert-ChecksumsMatchArtifact $ArtifactPath $sumsPath
    if ($fromSums -ne [string]$Manifest.artifactSha256) {
        throw "SHA256SUMS.txt ($fromSums) != manifest artifactSha256 ($($Manifest.artifactSha256))"
    }
    Write-Host "Release records finalized: artifactSha256=$actual (final distributed hash)" -ForegroundColor Green
    return [pscustomobject]@{
        ManifestPath   = $manifestPath
        SumsPath       = $sumsPath
        ArtifactSha256 = $actual
    }
}

function Test-SemanticVersion {
    <#
    .SYNOPSIS
        True when the value is a valid SemVer 2.0.0 core version (with an
        optional prerelease suffix). Build metadata is not part of the
        manifest semantic version.
    #>
    param([Parameter(Mandatory = $true)][string]$Version)
    return $Version -match '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$'
}

function Get-SemanticVersionPart {
    <#
    .SYNOPSIS
        Strips any '+<build-metadata>' suffix from an informational version,
        leaving the semantic version part.
    #>
    param([Parameter(Mandatory = $true)][string]$InformationalVersion)
    return $InformationalVersion.Split('+', 2)[0].Trim()
}

function Get-ProjectSemanticVersion {
    <#
    .SYNOPSIS
        Reads the authoritative semantic version from the project file's
        literal <Version> element. Refuses property expressions and malformed
        values: the manifest, the binary, and any workflow version input may
        only ever agree with the version the project itself declares.
    #>
    param([Parameter(Mandatory = $true)][string]$ProjectPath)
    if (-not (Test-Path -LiteralPath $ProjectPath -PathType Leaf)) {
        throw "Project file not found: $ProjectPath"
    }
    $text = [IO.File]::ReadAllText($ProjectPath)
    $match = [regex]::Match($text, '<Version>\s*([^<\s]+)\s*</Version>')
    if (-not $match.Success) {
        throw "No literal <Version> element found in $ProjectPath; the project version cannot be authoritative if it is not a literal."
    }
    $version = $match.Groups[1].Value.Trim()
    if ($version.Contains('$(')) {
        throw "Project <Version> is the property expression '$version'; refusing to guess an authoritative version."
    }
    if (-not (Test-SemanticVersion $version)) {
        throw "Project <Version> is not a valid semantic version: '$version'"
    }
    return $version
}

function Get-ReleaseTagFromVersion {
    <#
    .SYNOPSIS
        Derives the production release tag from a semantic version. The tag is
        always "v<semanticVersion>", never a free-form operator input, so the
        protected v* tag namespace and the v<semanticVersion> policy are
        structurally enforced.
    #>
    param([Parameter(Mandatory = $true)][string]$Version)
    if (-not (Test-SemanticVersion $Version)) {
        throw "Cannot derive a production release tag from a malformed semantic version: '$Version'"
    }
    return "v$Version"
}

function Assert-ReleaseTagMatchesVersion {
    <#
    .SYNOPSIS
        Fail-closed assertion that a proposed release tag equals the derived
        "v<semanticVersion>" production tag. The publication workflow no
        longer accepts a tag input (the tag is derived), so the adversarial
        tag states (v2.0.0 for version 1.0.0, "stable", ...) are proven
        impossible in the regression suite through this assertion.
    #>
    param(
        [Parameter(Mandatory = $true)][string]$Tag,
        [Parameter(Mandatory = $true)][string]$Version
    )
    if (-not (Test-SemanticVersion $Version)) {
        throw "Cannot validate a tag against a malformed semantic version: '$Version'"
    }
    $derived = "v$Version"
    if ($Tag -ne $derived) {
        throw "Release tag '$Tag' does not equal the derived production tag '$derived' (the production tag must be v<semanticVersion>)"
    }
    return $derived
}

function Test-CompletedAt {
    <#
    .SYNOPSIS
        Validates an evidence completion timestamp: it must parse as an
        ISO-8601 DateTimeOffset and must not be materially in the future
        (5-minute clock-skew tolerance).
    #>
    param([Parameter(Mandatory = $true)][string]$Value)
    $parsed = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse($Value, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind, [ref]$parsed)) {
        return [pscustomobject]@{ Valid = $false; Failure = "completedAt '$Value' is not a parseable ISO-8601 timestamp" }
    }
    $tolerance = [TimeSpan]::FromMinutes(5)
    if ($parsed.UtcDateTime -gt [DateTimeOffset]::UtcNow.UtcDateTime.Add($tolerance)) {
        return [pscustomobject]@{ Valid = $false; Failure = "completedAt '$Value' is in the future (beyond the 5-minute clock-skew tolerance)" }
    }
    return [pscustomobject]@{ Valid = $true; Failure = '' }
}

function Test-ExternalEvidenceFile {
    <#
    .SYNOPSIS
        Validates a production qualification evidence file
        (release-external-evidence.json) against the exact candidate SHA and
        the final distributed artifact hash.

    .DESCRIPTION
        The evidence file is the auditable record that the mandatory external
        gates (final human Windows smoke, physical mixed-DPI qualification,
        Windows 10/11 x64 compatibility) were performed against the exact
        bytes being published. It is bound to the candidate source SHA, to the
        final artifact hash, and (schema v3) to the exact Stage A workflow run
        and artifact that produced the candidate, so evidence from another
        candidate, another artifact, or another run can never be reused.

        A caller-controlled boolean is NOT accepted as evidence: only a
        schema-validated record with per-gate PASS status, operator,
        completion time (parseable ISO-8601, not materially in the future),
        and evidence detail passes. Anything else (missing file, malformed
        JSON, wrong schema version, wrong SHA, wrong artifact hash, wrong
        candidate run/artifact, FAIL, BLOCKED_EXTERNAL, missing fields) fails
        closed.

        Returns [pscustomobject]@{ Valid=[bool]; Failures=[string[]]; Evidence=$parsed }.
    #>
    param(
        [Parameter(Mandatory = $true)][string]$EvidencePath,
        [Parameter(Mandatory = $true)][string]$ExpectedSourceSha,
        [Parameter(Mandatory = $true)][string]$ExpectedArtifactSha,
        [string]$ExpectedCandidateRunId = '',
        [string]$ExpectedCandidateArtifactName = '',
        [string]$QualificationBundleRoot = '',
        [switch]$RequireQualificationBundle
    )
    $failures = [System.Collections.Generic.List[string]]::new()
    $evidence = $null

    if (-not (Test-Path -LiteralPath $EvidencePath -PathType Leaf)) {
        $failures.Add("external evidence file is missing: $EvidencePath")
        return [pscustomobject]@{ Valid = $false; Failures = @($failures); Evidence = $null }
    }
    $raw = ''
    try { $raw = [IO.File]::ReadAllText($EvidencePath) }
    catch {
        $failures.Add("external evidence file cannot be read: $($_.Exception.Message)")
        return [pscustomobject]@{ Valid = $false; Failures = @($failures); Evidence = $null }
    }
    if ([string]::IsNullOrWhiteSpace($raw)) {
        $failures.Add('external evidence file is empty')
        return [pscustomobject]@{ Valid = $false; Failures = @($failures); Evidence = $null }
    }
    try { $evidence = $raw | ConvertFrom-Json }
    catch { $failures.Add("external evidence is malformed JSON: $($_.Exception.Message)") }
    if ($null -eq $evidence) {
        return [pscustomobject]@{ Valid = $false; Failures = @($failures); Evidence = $null }
    }

    $evidenceSchema = [int]$evidence.schemaVersion
    if ($evidenceSchema -notin @(2, 3)) {
        $failures.Add("external evidence schemaVersion=$($evidence.schemaVersion) (expected 2 or 3)")
    }
    if ($RequireQualificationBundle -and $evidenceSchema -ne 3) {
        $failures.Add("external evidence schemaVersion=$evidenceSchema (schema 3 is required when qualification-bundle binding is required)")
    }
    $evSha = [string]$evidence.sourceCommitSha
    if ($evSha -notmatch '^[0-9a-f]{40}$') {
        $failures.Add("external evidence sourceCommitSha is malformed: '$evSha'")
    }
    elseif (-not [string]::Equals($evSha, $ExpectedSourceSha, [StringComparison]::OrdinalIgnoreCase)) {
        $failures.Add("external evidence sourceCommitSha $evSha != candidate SHA $ExpectedSourceSha")
    }
    $evHash = [string]$evidence.artifactSha256
    if ($evHash -notmatch '^[0-9a-f]{64}$') {
        $failures.Add("external evidence artifactSha256 is malformed: '$evHash'")
    }
    elseif (-not [string]::Equals($evHash, $ExpectedArtifactSha, [StringComparison]::OrdinalIgnoreCase)) {
        $failures.Add("external evidence artifactSha256 $evHash != final distributed artifact SHA-256 $ExpectedArtifactSha")
    }
    # Schema v2: the evidence must name the exact Stage A run and artifact that
    # produced the candidate, so a hash-match alone can never be sufficient
    # provenance ("artifact-name = something" is never accepted by itself).
    $evRunId = [string]$evidence.candidateWorkflowRunId
    if ($evRunId -notmatch '^[0-9]+$') {
        $failures.Add("external evidence candidateWorkflowRunId is malformed: '$evRunId' (expected the numeric Stage A workflow run ID)")
    }
    elseif (-not [string]::IsNullOrWhiteSpace($ExpectedCandidateRunId) -and $evRunId -ne $ExpectedCandidateRunId) {
        $failures.Add("external evidence candidateWorkflowRunId $evRunId != Stage A candidate run $ExpectedCandidateRunId")
    }
    $evArtifactName = [string]$evidence.candidateArtifactName
    if ([string]::IsNullOrWhiteSpace($evArtifactName)) {
        $failures.Add('external evidence candidateArtifactName is empty (the Stage A candidate artifact must be named)')
    }
    elseif (-not [string]::IsNullOrWhiteSpace($ExpectedCandidateArtifactName) -and $evArtifactName -ne $ExpectedCandidateArtifactName) {
        $failures.Add("external evidence candidateArtifactName $evArtifactName != the downloaded Stage A artifact $ExpectedCandidateArtifactName")
    }

    $bundleReference = $evidence.qualificationBundle
    if ($RequireQualificationBundle -or $null -ne $bundleReference) {
        $bundleRoot = if ([string]::IsNullOrWhiteSpace($QualificationBundleRoot)) { Split-Path -Parent ([IO.Path]::GetFullPath($EvidencePath)) } else { $QualificationBundleRoot }
        $binding = Test-QualificationEvidenceBinding -Evidence $evidence -EvidenceDirectory $bundleRoot `
            -ExpectedSourceSha $ExpectedSourceSha -ExpectedArtifactSha $ExpectedArtifactSha -RequirePhysicalTopology
        foreach ($failure in @($binding.Failures)) { $failures.Add([string]$failure) }
    }

    if ($RequireQualificationBundle) {
        # A production physical PASS is a structured observation, not a prose
        # claim. The bundle binding above proves the retained bundle bytes and
        # this record proves the observed topology was genuinely physical and
        # mixed-DPI. Synthetic, replay-only, blocked, skipped, and flaky data
        # therefore cannot be promoted by changing a status string.
        $physical = $evidence.physicalMixedDpi
        $observedTopology = if ($null -ne $physical) { $physical.observedTopology } else { $null }
        if ($null -eq $observedTopology) {
            $failures.Add('physicalMixedDpi is missing structured observedTopology')
        }
        else {
            if ([bool]$observedTopology.syntheticTopology) { $failures.Add('physicalMixedDpi observedTopology is synthetic') }
            if ([bool]$observedTopology.replayOnly) { $failures.Add('physicalMixedDpi observedTopology is replay-only') }
            if ([bool]$observedTopology.physicalGateEligible -ne $true) { $failures.Add('physicalMixedDpi observedTopology is not physicalGateEligible') }
            if ([int]$observedTopology.monitorCount -lt 2) { $failures.Add('physicalMixedDpi observedTopology has fewer than two monitors') }
            if ([bool]$observedTopology.mixedDpi -ne $true) { $failures.Add('physicalMixedDpi observedTopology did not observe mixed DPI') }
            $dpiValues = @($observedTopology.dpiValues | ForEach-Object { [int]$_ })
            if ($dpiValues.Count -lt 2 -or @($dpiValues | Where-Object { $_ -ne 96 }).Count -eq 0) { $failures.Add('physicalMixedDpi observedTopology has no non-default DPI value') }
        }
        $physicalBundleSha = [string]$physical.qualificationBundleSha256
        if ($physicalBundleSha -notmatch '^[0-9a-fA-F]{64}$') { $failures.Add('physicalMixedDpi is missing a valid qualificationBundleSha256') }
        elseif ($null -ne $bundleReference -and $physicalBundleSha -ne [string]$bundleReference.sha256) { $failures.Add('physicalMixedDpi qualificationBundleSha256 disagrees with qualificationBundle binding') }
        if ([string]$physical.candidateSha256 -ne $ExpectedArtifactSha) { $failures.Add('physicalMixedDpi candidateSha256 disagrees with the final artifact') }
        if ([string]$physical.runManifestSha256 -notmatch '^[0-9a-fA-F]{64}$') { $failures.Add('physicalMixedDpi is missing a valid runManifestSha256') }
        elseif ($null -ne $bundleReference -and [string]$physical.runManifestSha256 -ne [string]$bundleReference.primaryRunManifestSha256) { $failures.Add('physicalMixedDpi runManifestSha256 disagrees with the bound primary run') }
        $physicalMachine = $physical.machineReport
        if ($null -eq $physicalMachine) {
            $failures.Add('physicalMixedDpi is missing structured machineReport evidence')
        }
        else {
            $physicalMachineResult = Test-ExternalMachineReportBinding -Machine $physicalMachine -ArtifactRoot $bundleRoot -ExpectedSourceSha $ExpectedSourceSha -ExpectedArtifactSha $ExpectedArtifactSha -RequirePhysicalTopology
            foreach ($failure in @($physicalMachineResult.Failures)) { $failures.Add([string]$failure) }
            if ($null -ne $physicalMachineResult.Report) {
                $reportTopology = $physicalMachineResult.Report.topology
                foreach ($field in @('syntheticTopology', 'replayOnly', 'physicalGateEligible', 'monitorCount', 'mixedDpi')) {
                    if ([string](Get-QualificationProperty $reportTopology $field) -ne [string](Get-QualificationProperty $observedTopology $field)) { $failures.Add("physicalMixedDpi observedTopology.$field disagrees with the machine report") }
                }
            }
        }
    }

    foreach ($gate in @('finalWindowsHumanSmoke', 'physicalMixedDpi')) {
        $g = $evidence.$gate
        if ($null -eq $g) {
            $failures.Add("external evidence is missing mandatory gate '$gate'")
            continue
        }
        $status = [string]$g.status
        if ($status -ne 'PASS') {
            $failures.Add("external gate '$gate' status=$status (only PASS is acceptable for production publication)")
        }
        if ([string]::IsNullOrWhiteSpace([string]$g.operator)) {
            $failures.Add("external gate '$gate' is missing 'operator'")
        }
        $gateAt = [string]$g.completedAt
        if ([string]::IsNullOrWhiteSpace($gateAt)) {
            $failures.Add("external gate '$gate' is missing 'completedAt'")
        }
        else {
            $at = Test-CompletedAt $gateAt
            if (-not $at.Valid) { $failures.Add("external gate '$gate' $($at.Failure)") }
        }
        if ([string]::IsNullOrWhiteSpace([string]$g.evidence)) {
            $failures.Add("external gate '$gate' is missing 'evidence'")
        }
    }

    # Windows compatibility gate: v1.0.0 advertises Windows 10 and Windows 11
    # x64, so both must carry PASS evidence recorded on real machines (build
    # recorded, native ABI self-test evidence attached) before production
    # publication. Missing/malformed/FAIL/BLOCKED entries fail closed.
    $wc = $evidence.windowsCompatibility
    if ($null -eq $wc) {
        $failures.Add("external evidence is missing mandatory gate 'windowsCompatibility'")
    }
    else {
        if ([string]$wc.status -ne 'PASS') {
            $failures.Add("external gate 'windowsCompatibility' status=$($wc.status) (only PASS is acceptable for production publication)")
        }
        foreach ($os in @('windows10', 'windows11')) {
            $osGate = $wc.$os
            if ($null -eq $osGate) {
                $failures.Add("windowsCompatibility is missing mandatory entry '$os'")
                continue
            }
            $osStatus = [string]$osGate.status
            if ($osStatus -ne 'PASS') {
                $failures.Add("windowsCompatibility.$os status=$osStatus (only PASS is acceptable for production publication)")
            }
            if ([string]::IsNullOrWhiteSpace([string]$osGate.build)) {
                $failures.Add("windowsCompatibility.$os is missing 'build' (OS build must be recorded)")
            }
            if ([string]::IsNullOrWhiteSpace([string]$osGate.operator)) {
                $failures.Add("windowsCompatibility.$os is missing 'operator'")
            }
            $osAt = [string]$osGate.completedAt
            if ([string]::IsNullOrWhiteSpace($osAt)) {
                $failures.Add("windowsCompatibility.$os is missing 'completedAt'")
            }
            else {
                $at = Test-CompletedAt $osAt
                if (-not $at.Valid) { $failures.Add("windowsCompatibility.$os $($at.Failure)") }
            }
            if ([string]::IsNullOrWhiteSpace([string]$osGate.nativeAbiEvidence)) {
                $failures.Add("windowsCompatibility.$os is missing 'nativeAbiEvidence' (the --selftest-native-abi environment report must be recorded)")
            }
            if ([string]::IsNullOrWhiteSpace([string]$osGate.evidence)) {
                $failures.Add("windowsCompatibility.$os is missing 'evidence'")
            }
            if ($RequireQualificationBundle) {
                $machine = $osGate.machineReport
                if ($null -eq $machine) {
                    $failures.Add("windowsCompatibility.$os is missing structured machineReport evidence")
                }
                else {
                    if ([string]$machine.sourceCommitSha -ne $ExpectedSourceSha) { $failures.Add("windowsCompatibility.$os machineReport sourceCommitSha disagrees with the candidate") }
                    if ([string]$machine.candidateSha256 -ne $ExpectedArtifactSha) { $failures.Add("windowsCompatibility.$os machineReport candidateSha256 disagrees with the final artifact") }
                    $expectedFamily = if ($os -eq 'windows10') { 'Windows 10' } else { 'Windows 11' }
                    if ([string]$machine.osFamily -ne $expectedFamily) { $failures.Add("windowsCompatibility.$os machineReport osFamily must be '$expectedFamily'") }
                    if ([string]$machine.architecture -notin @('X64', 'AMD64')) { $failures.Add("windowsCompatibility.$os machineReport architecture is not x64") }
                    if ([string]$machine.nativeAbiResult -ne 'PASS') { $failures.Add("windowsCompatibility.$os machineReport nativeAbiResult is not PASS") }
                    if ([string]$machine.qualificationBundleSha256 -notmatch '^[0-9a-fA-F]{64}$') { $failures.Add("windowsCompatibility.$os machineReport qualificationBundleSha256 is malformed") }
                    if ([string]$machine.runManifestSha256 -notmatch '^[0-9a-fA-F]{64}$') { $failures.Add("windowsCompatibility.$os machineReport runManifestSha256 is malformed") }
                    if ([string]$machine.reportSha256 -notmatch '^[0-9a-fA-F]{64}$') { $failures.Add("windowsCompatibility.$os machineReport reportSha256 is malformed") }
                    if ([string]$machine.verificationStatus -ne 'PASS') { $failures.Add("windowsCompatibility.$os machineReport verificationStatus is not PASS") }
                    if ([string]::IsNullOrWhiteSpace([string]$machine.qualificationBundleRelativePath)) { $failures.Add("windowsCompatibility.$os machineReport qualificationBundleRelativePath is missing") }
                    $machinePath = [string]$machine.relativePath
                    if (-not [string]::IsNullOrWhiteSpace($machinePath) -and -not [string]::IsNullOrWhiteSpace($QualificationBundleRoot)) {
                        try {
                            $resolvedMachine = Resolve-QualificationRelativePath -Root $QualificationBundleRoot -RelativePath $machinePath
                            if (-not (Test-Path -LiteralPath $resolvedMachine.FullPath -PathType Leaf)) { $failures.Add("windowsCompatibility.$os machineReport is missing: '$machinePath'") }
                            elseif ((Get-QualificationFileSha256 $resolvedMachine.FullPath) -ne [string]$machine.reportSha256) { $failures.Add("windowsCompatibility.$os machineReport hash disagrees: '$machinePath'") }
                        }
                        catch { $failures.Add("windowsCompatibility.$os machineReport path is invalid: $($_.Exception.Message)") }
                    }
                    $machineResult = Test-ExternalMachineReportBinding -Machine $machine -ArtifactRoot $QualificationBundleRoot -ExpectedSourceSha $ExpectedSourceSha -ExpectedArtifactSha $ExpectedArtifactSha
                    foreach ($failure in @($machineResult.Failures)) { $failures.Add([string]$failure) }
                }
            }
        }
    }
    return [pscustomobject]@{ Valid = $failures.Count -eq 0; Failures = @($failures); Evidence = $evidence }
}

function Test-ExternalMachineReportBinding {
    <#
    Validates one structured machine report and the qualification bundle it
    references. This helper is data-only: it never launches a returned
    executable, script, or process.
    #>
    param(
        [Parameter(Mandatory = $true)][object]$Machine,
        [Parameter(Mandatory = $true)][string]$ArtifactRoot,
        [Parameter(Mandatory = $true)][string]$ExpectedSourceSha,
        [Parameter(Mandatory = $true)][string]$ExpectedArtifactSha,
        [switch]$RequirePhysicalTopology
    )
    $failures = [System.Collections.Generic.List[string]]::new()
    $machinePath = [string](Get-QualificationProperty $Machine 'relativePath')
    $report = $null
    $reportFullPath = $null
    if ([string]::IsNullOrWhiteSpace($machinePath)) {
        [void]$failures.Add('structured machineReport.relativePath is missing')
    }
    else {
        try { $reportFullPath = (Resolve-QualificationRelativePath -Root $ArtifactRoot -RelativePath $machinePath).FullPath }
        catch { [void]$failures.Add("structured machineReport.relativePath is invalid: $($_.Exception.Message)") }
        if ($null -ne $reportFullPath) {
            if (-not (Test-Path -LiteralPath $reportFullPath -PathType Leaf)) {
                [void]$failures.Add("structured machineReport is missing: '$machinePath'")
            }
            else {
                $recordedReportSha = [string](Get-QualificationProperty $Machine 'reportSha256')
                if ($recordedReportSha -notmatch '^[0-9a-fA-F]{64}$') { [void]$failures.Add('structured machineReport.reportSha256 is malformed') }
                elseif (-not [string]::Equals((Get-QualificationFileSha256 $reportFullPath), $recordedReportSha, [StringComparison]::OrdinalIgnoreCase)) { [void]$failures.Add("structured machineReport hash disagrees: '$machinePath'") }
                try {
                    $reportJson = Read-QualificationJsonFile $reportFullPath
                    foreach ($failure in @($reportJson.DuplicateFailures)) { [void]$failures.Add([string]$failure) }
                    $report = $reportJson.Value
                }
                catch { [void]$failures.Add("structured machineReport is not strict JSON: $($_.Exception.Message)") }
            }
        }
    }
    if ($null -eq $report) { return [pscustomobject]@{ Valid = $false; Failures = @($failures); Report = $null; Bundle = $null } }

    $reportKind = [string](Get-QualificationProperty $report 'reportKind')
    if ($reportKind -notin @('independent-machine-qualification', 'external-machine-evidence')) {
        [void]$failures.Add("structured machineReport reportKind '$reportKind' is unsupported")
    }
    $reportSource = [string](Get-QualificationProperty $report 'sourceCommitSha')
    $reportCandidate = [string](Get-QualificationProperty $report 'candidateSha256')
    if (-not [string]::Equals($reportSource, $ExpectedSourceSha, [StringComparison]::OrdinalIgnoreCase)) { [void]$failures.Add('structured machineReport sourceCommitSha disagrees with the final candidate') }
    if (-not [string]::Equals($reportCandidate, $ExpectedArtifactSha, [StringComparison]::OrdinalIgnoreCase)) { [void]$failures.Add('structured machineReport candidateSha256 disagrees with the final candidate') }
    if (-not [string]::Equals([string](Get-QualificationProperty $Machine 'sourceCommitSha'), $ExpectedSourceSha, [StringComparison]::OrdinalIgnoreCase)) { [void]$failures.Add('machineReport binding sourceCommitSha disagrees with the final candidate') }
    if (-not [string]::Equals([string](Get-QualificationProperty $Machine 'candidateSha256'), $ExpectedArtifactSha, [StringComparison]::OrdinalIgnoreCase)) { [void]$failures.Add('machineReport binding candidateSha256 disagrees with the final candidate') }
    $osFamily = [string](Get-QualificationProperty $Machine 'osFamily')
    if ($osFamily -notin @('Windows 10', 'Windows 11')) { [void]$failures.Add("structured machineReport osFamily '$osFamily' is unsupported") }
    if ([string](Get-QualificationProperty $Machine 'architecture') -notin @('X64', 'AMD64')) { [void]$failures.Add('structured machineReport architecture is not x64') }
    if ([string](Get-QualificationProperty $Machine 'nativeAbiResult') -ne 'PASS') { [void]$failures.Add('structured machineReport nativeAbiResult is not PASS') }
    if ([string](Get-QualificationProperty $Machine 'verificationStatus') -ne 'PASS') { [void]$failures.Add('structured machineReport verificationStatus is not PASS') }
    $reportPrivacy = Get-QualificationProperty $report 'privacy'
    if ($null -ne $reportPrivacy -and ([bool](Get-QualificationProperty $reportPrivacy 'privacySafe') -ne $true -or [bool](Get-QualificationProperty $reportPrivacy 'containsRawDesktopData') -ne $false)) {
        [void]$failures.Add('structured machineReport privacy contract is not explicitly safe')
    }

    $bundleRelative = [string](Get-QualificationProperty $Machine 'qualificationBundleRelativePath')
    $bundle = $null
    $bundlePath = $null
    if ([string]::IsNullOrWhiteSpace($bundleRelative)) {
        [void]$failures.Add('structured machineReport.qualificationBundleRelativePath is missing')
    }
    else {
        try { $bundlePath = (Resolve-QualificationRelativePath -Root $ArtifactRoot -RelativePath $bundleRelative).FullPath }
        catch { [void]$failures.Add("structured machineReport qualification bundle path is invalid: $($_.Exception.Message)") }
        if ($null -ne $bundlePath) {
            if (-not (Test-Path -LiteralPath $bundlePath -PathType Leaf)) {
                [void]$failures.Add("structured machineReport qualification bundle is missing: '$bundleRelative'")
            }
            else {
                $bundleSha = Get-QualificationFileSha256 $bundlePath
                $expectedBundleSha = [string](Get-QualificationProperty $Machine 'qualificationBundleSha256')
                if (-not [string]::Equals($bundleSha, $expectedBundleSha, [StringComparison]::OrdinalIgnoreCase)) { [void]$failures.Add('structured machineReport qualificationBundleSha256 disagrees with the referenced bundle') }
                $bundleVerification = Test-QualificationBundle -BundlePath $bundlePath -ExpectedSourceSha $ExpectedSourceSha -ExpectedArtifactSha $ExpectedArtifactSha -RequirePhysicalTopology:$RequirePhysicalTopology
                foreach ($failure in @($bundleVerification.Failures)) { [void]$failures.Add([string]$failure) }
                $bundle = $bundleVerification.Bundle
                if ($null -ne $bundle) {
                    $primaryRelative = [string](Get-QualificationProperty $bundle 'primaryRunManifest')
                    $primaryEntry = @((Get-QualificationProperty $bundle 'runManifests') | Where-Object { [string](Get-QualificationProperty $_ 'relativePath') -eq $primaryRelative }) | Select-Object -First 1
                    if ($null -ne $primaryEntry -and -not [string]::Equals([string](Get-QualificationProperty $Machine 'runManifestSha256'), [string](Get-QualificationProperty $primaryEntry 'sha256'), [StringComparison]::OrdinalIgnoreCase)) { [void]$failures.Add('structured machineReport runManifestSha256 disagrees with the bundle primary run') }
                }
            }
        }
    }

    $reportBundleRelative = [string](Get-QualificationProperty $report 'qualificationBundleRelativePath')
    if (-not [string]::IsNullOrWhiteSpace($reportBundleRelative) -and $null -ne $reportFullPath) {
        try {
            $reportBundlePath = (Resolve-QualificationRelativePath -Root ([IO.Path]::GetDirectoryName($reportFullPath)) -RelativePath $reportBundleRelative).FullPath
            if (-not (Test-Path -LiteralPath $reportBundlePath -PathType Leaf)) { [void]$failures.Add('structured machineReport internal qualification bundle is missing') }
            elseif (-not [string]::Equals((Get-QualificationFileSha256 $reportBundlePath), [string](Get-QualificationProperty $report 'qualificationBundleSha256'), [StringComparison]::OrdinalIgnoreCase)) { [void]$failures.Add('structured machineReport internal qualification bundle hash disagrees') }
        }
        catch { [void]$failures.Add("structured machineReport internal qualification bundle path is invalid: $($_.Exception.Message)") }
    }

    if ($RequirePhysicalTopology) {
        $topology = Get-QualificationProperty $report 'topology'
        if ([bool](Get-QualificationProperty $topology 'syntheticTopology') -or [bool](Get-QualificationProperty $topology 'replayOnly')) { [void]$failures.Add('structured physical machineReport topology is synthetic or replay-only') }
        if ([bool](Get-QualificationProperty $topology 'physicalGateEligible') -ne $true) { [void]$failures.Add('structured physical machineReport topology is not physicalGateEligible') }
        if ([int](Get-QualificationProperty $topology 'monitorCount') -lt 2) { [void]$failures.Add('structured physical machineReport observed fewer than two monitors') }
        if ([bool](Get-QualificationProperty $topology 'mixedDpi') -ne $true) { [void]$failures.Add('structured physical machineReport did not observe mixed DPI') }
        $dpis = @(Get-QualificationProperty $topology 'dpiValues') | ForEach-Object { [int]$_ }
        if ($dpis.Count -lt 2 -or @($dpis | Where-Object { $_ -ne 96 }).Count -eq 0) { [void]$failures.Add('structured physical machineReport has no non-default DPI value') }
    }
    [pscustomobject]@{ Valid = $failures.Count -eq 0; Failures = @($failures); Report = $report; Bundle = $bundle }
}

function Find-Signtool {
    <#
    .SYNOPSIS
        Locates signtool.exe from the installed Windows SDK (highest version
        first) or PATH. Returns $null when unavailable.
    #>
    $candidates = @()
    $kitRoot = 'C:\Program Files (x86)\Windows Kits\10\bin'
    if (Test-Path -LiteralPath $kitRoot) {
        foreach ($versionDir in (Get-ChildItem -LiteralPath $kitRoot -Directory | Sort-Object Name -Descending)) {
            $candidates += Join-Path $versionDir.FullName 'x64\signtool.exe'
        }
    }
    $command = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($null -ne $command) { $candidates += $command.Source }
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }
    return $null
}

function Test-AuthenticodeSignature {
    <#
    .SYNOPSIS
        Independently proves the executable carries a valid Authenticode
        signature by running `signtool verify /pa /v /tw` (no signing material
        is needed to verify). Fail-closed: returns $false when signtool is
        unavailable or verification fails. Verification is never faked.
        /tw additionally makes an untimestamped signature a WARNING result
        (signtool exit code 2), which fails this check like any non-zero exit.
    #>
    param([Parameter(Mandatory = $true)][string]$ExePath)
    if (-not (Test-Path -LiteralPath $ExePath -PathType Leaf)) {
        Write-Host "Test-AuthenticodeSignature: executable not found: $ExePath" -ForegroundColor Yellow
        return $false
    }
    $signtool = Find-Signtool
    if ($null -eq $signtool) {
        Write-Host 'Test-AuthenticodeSignature: signtool.exe not found; treating the artifact as unsigned (fail closed)' -ForegroundColor Yellow
        return $false
    }
    & $signtool @('verify', '/pa', '/v', '/tw', $ExePath) 2>&1 | Out-Null
    $ok = $LASTEXITCODE -eq 0
    if (-not $ok) {
        Write-Host "Test-AuthenticodeSignature: signtool verify /pa failed (exit $LASTEXITCODE) for $ExePath" -ForegroundColor Red
    }
    return $ok
}

function Test-AuthenticodeTimestamp {
    <#
    .SYNOPSIS
        Independently proves the executable carries a valid RFC3161 timestamp
        in addition to its Authenticode signature. Fail-closed: returns
        $false when the file is unsigned, the signature is not valid, no
        timestamp certificate is present, signtool is unavailable, or
        `signtool verify /pa /v /tw` does not pass (the Authenticode policy
        validates the timestamp chain when one is present, and /tw makes a
        missing timestamp a warning result - exit code 2 - which fails this
        check like any non-zero exit).

    .DESCRIPTION
        Passing /tr to a signer is NOT treated as proof of timestamping: the
        timestamp is only accepted after Windows tooling validates it and a
        timestamp certificate is visible on the signature. Verification is
        provider-independent and never faked.
    #>
    param([Parameter(Mandatory = $true)][string]$ExePath)
    if (-not (Test-Path -LiteralPath $ExePath -PathType Leaf)) {
        Write-Host "Test-AuthenticodeTimestamp: executable not found: $ExePath" -ForegroundColor Yellow
        return $false
    }
    $sig = Get-AuthenticodeSignature -LiteralPath $ExePath
    if ($null -eq $sig -or $sig.Status -ne 'Valid') {
        Write-Host 'Test-AuthenticodeTimestamp: no valid Authenticode signature found; timestamp cannot be verified (fail closed)' -ForegroundColor Yellow
        return $false
    }
    if ($null -eq $sig.TimeStamperCertificate) {
        Write-Host 'Test-AuthenticodeTimestamp: no RFC3161 timestamp certificate is present on the signature (fail closed)' -ForegroundColor Red
        return $false
    }
    $signtool = Find-Signtool
    if ($null -eq $signtool) {
        Write-Host 'Test-AuthenticodeTimestamp: signtool.exe not found; timestamp chain cannot be validated (fail closed)' -ForegroundColor Yellow
        return $false
    }
    & $signtool @('verify', '/pa', '/v', '/tw', $ExePath) 2>&1 | Out-Null
    $ok = $LASTEXITCODE -eq 0
    if (-not $ok) {
        Write-Host "Test-AuthenticodeTimestamp: signtool verify /pa failed (exit $LASTEXITCODE); the timestamp/signature chain is not valid" -ForegroundColor Red
    }
    return $ok
}

function Get-SignerCertificateInfo {
    <#
    .SYNOPSIS
        Extracts the signing certificate identity from an Authenticode-signed
        executable using Windows' own signature reader (Get-AuthenticodeSignature):
        subject, thumbprint, issuer, serial number, validity period, EKU OIDs,
        and the timestamp certificate when present.

    .DESCRIPTION
        Returns $null when the file is unsigned, the signature is not
        Status=Valid, or no signer certificate can be read (fail closed:
        absence is never reported as an identity). Used by
        scripts/sign-release.ps1 for the signer contract and by the Stage B
        gate to cross-check the manifest's recorded certificate identity
        against the actual bytes.
    #>
    param([Parameter(Mandatory = $true)][string]$ExePath)
    if (-not (Test-Path -LiteralPath $ExePath -PathType Leaf)) {
        return $null
    }
    $sig = Get-AuthenticodeSignature -LiteralPath $ExePath
    if ($null -eq $sig -or $sig.Status -ne 'Valid' -or $null -eq $sig.SignerCertificate) {
        return $null
    }
    $cert = $sig.SignerCertificate
    $eku = [System.Collections.Generic.List[string]]::new()
    $ekuExt = $cert.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.37' } | Select-Object -First 1
    if ($null -ne $ekuExt) {
        # Typed X509EnhancedKeyUsageExtension exposes EnhancedKeyUsages; fall
        # back to parsing Format() output when the typed property is absent.
        if ($null -ne $ekuExt.EnhancedKeyUsages) {
            foreach ($oid in $ekuExt.EnhancedKeyUsages) { $eku.Add([string]$oid.Value) }
        }
        else {
            foreach ($m in [regex]::Matches([string]$ekuExt.Format($true), '\(([0-9]+(?:\.[0-9]+)+)\)')) {
                $eku.Add($m.Groups[1].Value)
            }
        }
    }
    return [pscustomobject]@{
        Subject               = [string]$cert.Subject
        Thumbprint            = [string]$cert.Thumbprint
        Issuer                = [string]$cert.Issuer
        SerialNumber          = [string]$cert.SerialNumber
        ValidFrom             = $cert.NotBefore.ToString('O')
        ValidTo               = $cert.NotAfter.ToString('O')
        Eku                   = @($eku)
        TimestamperSubject    = if ($null -ne $sig.TimeStamperCertificate) { [string]$sig.TimeStamperCertificate.Subject } else { $null }
        TimestamperThumbprint = if ($null -ne $sig.TimeStamperCertificate) { [string]$sig.TimeStamperCertificate.Thumbprint } else { $null }
    }
}

function Test-CertificateEkuIncludesCodeSigning {
    <#
    .SYNOPSIS
        True when the certificate's EKU OID list includes the code-signing EKU
        (1.3.6.1.5.5.7.3.3). An empty or absent EKU list is never accepted.
    #>
    param($Eku)
    $oids = @($Eku | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) })
    return $oids -contains '1.3.6.1.5.5.7.3.3'
}

# --- Signing provider policy ------------------------------------------------
# The signer abstraction: release-qualify.ps1 and the Stage A workflow never
# know or care HOW signing happens. SIGNING_PROVIDER selects the backend and
# every provider reports the SAME structured contract (Status, Verification,
# FinalSha256, Provider, KeyProtection, TimestampStatus, certificate
# identity). Production policy accepts ONLY providers whose private signing
# key is non-exportable (CLOUD_HSM class); local-PFX (exportable key),
# mock, and unconfigured signers are development/RC-only.

function Get-SigningProvider {
    <#
    .SYNOPSIS
        Returns the normalized signing provider selected by the
        SIGNING_PROVIDER environment variable. Empty/absent means
        'not-configured'. Any unrecognized value throws so that a typo can
        never silently fall back to unsigned or to another provider.
    #>
    $value = [string]$env:SIGNING_PROVIDER
    if ([string]::IsNullOrWhiteSpace($value)) {
        return 'not-configured'
    }
    $normalized = $value.Trim().ToLowerInvariant()
    $known = @('not-configured', 'local-pfx', 'digicert-stm', 'mock-test')
    if ($known -notcontains $normalized) {
        throw "Unknown SIGNING_PROVIDER '$value'; supported providers: $($known -join ', '). Production candidates require an approved HSM/cloud signing provider (currently 'digicert-stm')."
    }
    return $normalized
}

function Get-SigningProviderKeyProtection {
    <#
    .SYNOPSIS
        The private-key protection classification for a signing provider.
        CLOUD_HSM = non-exportable key held by a signing service/HSM
        (production-approved class); LOCAL_PFX = exportable PFX key;
        MOCK_TEST = deterministic test scaffolding; NOT_CONFIGURED = none.
    #>
    param([Parameter(Mandatory = $true)][string]$Provider)
    switch ($Provider) {
        'local-pfx'      { return 'LOCAL_PFX' }
        'digicert-stm'   { return 'CLOUD_HSM' }
        'mock-test'      { return 'MOCK_TEST' }
        'not-configured' { return 'NOT_CONFIGURED' }
        default { throw "Unknown signing provider '$Provider'; cannot classify key protection." }
    }
}

function Get-ApprovedProductionSigningProviders {
    <#
    .SYNOPSIS
        The allowlist of signing providers approved for PRODUCTION Stage A
        candidates. Only non-exportable-key backends belong here. Future
        providers (for example Microsoft Artifact Signing, which is optional
        and geography-restricted for Public Trust) must be added here
        EXPLICITLY by repository policy before they can satisfy production.
    #>
    return @('digicert-stm')
}

function Test-ApprovedProductionSigningProvider {
    # [AllowEmptyString]: an empty provider is meaningfully 'not configured'
    # and must return $false rather than fail binding.
    param(
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Provider
    )
    return (Get-ApprovedProductionSigningProviders) -contains $Provider
}

function Get-ApprovedProductionKeyProtection {
    <#
    .SYNOPSIS
        The key-protection classifications that satisfy production policy.
        Only the non-exportable/hardware-backed class is approved.
    #>
    return @('CLOUD_HSM')
}

function Test-ApprovedProductionKeyProtection {
    # [AllowEmptyString]: an empty protection value is meaningfully
    # 'unclassified' and must return $false rather than fail binding.
    param(
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$KeyProtection
    )
    return (Get-ApprovedProductionKeyProtection) -contains $KeyProtection
}

function Get-SigningProviderCredentialRequirements {
    <#
    .SYNOPSIS
        The environment-variable NAMES (never values) a provider needs.
        digicert-stm credentials authenticate to the DigiCert Software Trust
        Manager signing service (API key + client-authentication certificate)
        and identify the keypair alias; they NEVER contain or export the
        production code-signing private key, which stays inside the
        service/HSM. local-pfx needs the exportable PFX and its password.
    #>
    param([Parameter(Mandatory = $true)][string]$Provider)
    switch ($Provider) {
        'local-pfx'    { return @('SIGNCERT_BASE64', 'SIGNCERT_PASSWORD') }
        'digicert-stm' { return @('SM_HOST', 'SM_API_KEY', 'SM_CLIENT_CERT_FILE', 'SM_CLIENT_CERT_PASSWORD', 'SM_KEYPAIR_ALIAS') }
        default        { return @() }
    }
}

function Test-SigningProviderConfiguration {
    <#
    .SYNOPSIS
        Fail-closed provider preflight: checks that ONLY the variables the
        selected provider requires are present. Returns a structured result
        with the missing variable NAMES; never prints or returns their
        values. SM_CLIENT_CERT_FILE additionally must name an existing file
        (a client-authentication certificate path that does not exist cannot
        authenticate).
    #>
    param([Parameter(Mandatory = $true)][string]$Provider)
    $missing = [System.Collections.Generic.List[string]]::new()
    foreach ($name in (Get-SigningProviderCredentialRequirements $Provider)) {
        $value = [Environment]::GetEnvironmentVariable($name)
        $present = switch ($name) {
            'SM_CLIENT_CERT_FILE' {
                (-not [string]::IsNullOrWhiteSpace($value)) -and (Test-Path -LiteralPath $value -PathType Leaf)
            }
            default { -not [string]::IsNullOrWhiteSpace($value) }
        }
        if (-not $present) { $missing.Add($name) }
    }
    return [pscustomobject]@{
        Provider      = $Provider
        KeyProtection = (Get-SigningProviderKeyProtection $Provider)
        Configured    = $missing.Count -eq 0
        Missing       = @($missing)
    }
}

function Test-ProductionSigningPreflight {
    <#
    .SYNOPSIS
        The production-ready preflight used by the Stage A workflow and by
        release-qualify.ps1 BEFORE any build work: resolves the provider,
        requires an APPROVED production provider, and requires its
        credentials to be complete. Returns a structured verdict; the caller
        fails the run (BLOCKED_EXTERNAL) when Approved or Configured is
        $false. Never prints secrets - only provider names and the missing
        variable names.
    #>
    $provider = Get-SigningProvider
    $cfg = Test-SigningProviderConfiguration $provider
    $approved = Test-ApprovedProductionSigningProvider $provider
    $publisher = Get-PublisherIdentityPolicy
    $blocked = ''
    if (-not $approved) {
        $blocked = "signing provider '$provider' is not an approved production signer (approved: $((Get-ApprovedProductionSigningProviders) -join ', ')); local-PFX, mock, and unconfigured signers are never production candidates"
    }
    elseif (-not $cfg.Configured) {
        $blocked = "signing provider '$provider' is missing required configuration: $($cfg.Missing -join ', ')"
    }
    elseif ([string]::IsNullOrWhiteSpace($publisher)) {
        $blocked = 'the current production publisher identity policy is not configured (SIGNING_EXPECTED_SUBJECT); production candidates require the expected publisher subject so the signed certificate is bound to the CURRENT publisher policy, never merely to the manifest'
    }
    return [pscustomobject]@{
        Provider          = $provider
        KeyProtection     = $cfg.KeyProtection
        Approved          = $approved
        Configured        = $cfg.Configured
        Missing           = $cfg.Missing
        PublisherIdentity = $publisher
        BlockedReason     = $blocked
    }
}

# --- Release-policy schema and publisher-identity policy ---------------------
# The release policy is versioned so a candidate is NEVER evaluated under the
# policy generation that produced it. Stage A records the CURRENT schema in
# the production manifest; Stage B (running the CURRENT module revision from
# the trusted policy checkout) requires manifest schema >= the minimum and
# evaluates every condition with CURRENT policy code.

function Get-ReleasePolicySchemaVersion {
    <#
    .SYNOPSIS
        The release-policy schema version of the CURRENT trusted policy
        implementation (this module revision). Stage A records it in the
        production manifest (release-manifest.json releasePolicySchemaVersion)
        so the schema contract is part of the candidate's provenance.
    #>
    return 3
}

function Get-MinimumAcceptedProductionPolicySchema {
    <#
    .SYNOPSIS
        The oldest release-policy schema generation the CURRENT trusted policy
        accepts for production publication. Older candidates (schema absent or
        below the minimum) fail closed: they were produced under an older
        release policy and are never silently re-evaluated under their own
        historical rules.

        Schema generations:
          1 = pre-provider two-stage era (candidate-controlled publication
              policy; no provider allowlist, no key-protection policy, no
              publisher-identity policy)
          2 = provider-allowlist era (approved-provider + CLOUD_HSM key
              protection policy, but no schema contract, no mandatory
              publisher identity, no timestamper identity)
          3 = current: schema contract + mandatory publisher identity +
              timestamper identity + trusted-policy isolation
    #>
    return 3
}

function Test-ReleasePolicySchema {
    <#
    .SYNOPSIS
        Fail-closed check that a release manifest records a production policy
        schema the CURRENT policy accepts: present, numeric, and >=
        Get-MinimumAcceptedProductionPolicySchema. Returns
        [pscustomobject]@{ Valid=[bool]; Failure=[string] }.
    #>
    param([Parameter(Mandatory = $true)]$Manifest)
    $raw = [string]$Manifest.releasePolicySchemaVersion
    if ($raw -notmatch '^\d+$') {
        return [pscustomobject]@{
            Valid   = $false
            Failure = "manifest releasePolicySchemaVersion is absent or malformed ('$raw'); a candidate produced before the current release-policy schema contract is never evaluated under the CURRENT policy"
        }
    }
    $schema = [int]$raw
    $minimum = Get-MinimumAcceptedProductionPolicySchema
    if ($schema -lt $minimum) {
        return [pscustomobject]@{
            Valid   = $false
            Failure = "manifest releasePolicySchemaVersion $schema < minimum accepted production policy schema $minimum; the candidate was produced under an older release policy and is rejected under the CURRENT policy (an old candidate does not become valid because its old policy would have accepted itself)"
        }
    }
    return [pscustomobject]@{ Valid = $true; Failure = '' }
}

function Get-PublisherIdentityPolicy {
    <#
    .SYNOPSIS
        The CURRENT expected publisher identity (SIGNING_EXPECTED_SUBJECT, a
        repository variable - only the NAME is ever discussed, never a value)
        that every production signed certificate must match. It is a stable
        publisher/subject identity, deliberately NOT a rotating certificate
        thumbprint, so certificate renewal does not require source changes.
        Empty when the current policy is not configured.
    #>
    return [string]$env:SIGNING_EXPECTED_SUBJECT
}

function Test-PublicationEligibility {
    <#
    .SYNOPSIS
        The fail-closed publication gate used by the release workflow's
        publish job before `gh release create`.

    .DESCRIPTION
        Verifies every production condition against the on-disk artifact and
        its records:
          - manifest automated qualification == PASS
          - manifest sourceCommitSha == requested SHA
          - manifest semanticVersion == requested version AND is a valid
            semantic version, with the binary identity recorded in the
            manifest (buildIdentity.semanticVersion / informationalVersion)
            agreeing with it (the csproj <Version> is the root authority,
            enforced at qualification time and re-checked by the publication
            workflow against the checked-out project file)
          - manifest releaseMode == PRODUCTION (qualification-only/RC
            artifacts can never be published)
          - manifest workflowRunId == the Stage A candidate run id
          - actual file hash == manifest.artifactSha256
          - SHA256SUMS.txt == actual file hash (triple consistency)
          - external evidence (schema v3) valid, source SHA == requested SHA,
            artifact SHA == final distributed artifact hash, candidate
            workflow run + artifact == the exact Stage A run and artifact
            being published, finalWindowsHumanSmoke == PASS,
            physicalMixedDpi == PASS, windowsCompatibility == PASS for
            Windows 10 and Windows 11
          - when production signing is mandatory: signingStatus == SIGNED,
            signatureVerification == SIGNATURE_VERIFIED,
            finalSignedSha256 == final artifact hash, the artifact is NOT a
            test-only mock-signed artifact, the manifest records an APPROVED
            production signing provider (non-exportable HSM/cloud key class;
            local-PFX, mock, not-configured, and unknown providers are
            rejected), the manifest records the signed certificate identity
            (subject, thumbprint, issuer, validity window, code-signing EKU)
            and the RFC3161 timestamper identity (subject, thumbprint), and
            timestampStatus == VERIFIED
          - the CURRENT release-policy schema contract: the manifest must
            record releasePolicySchemaVersion >= the current minimum, so a
            candidate produced under an older policy generation (or with no
            schema at all) is never evaluated under its own historical rules
          - the CURRENT trusted publisher policy: when production signing is
            mandatory, ExpectedPublisherSubject (SIGNING_EXPECTED_SUBJECT from
            the trusted policy revision) must be configured and must equal the
            manifest signingCertificateSubject AND the signed certificate
            subject read from the actual bytes - an artifact and its manifest
            that consistently record the WRONG publisher still fail
          - `signtool verify /pa /v /tw` + RFC3161 timestamp verification
            independently confirm the signature on disk and cross-check the
            recorded certificate identity against the actual bytes
        Any failure is returned (never thrown) as a Failures list; the caller
        must refuse to publish when Eligible is $false.

        The Stage B caller MUST supply the exact candidate run id and artifact
        name it downloaded (mandatory parameters): "artifact-name = something"
        is never accepted as provenance without the full run binding.

        Returns [pscustomobject]@{ Eligible=[bool]; Failures=[string[]] }.
    #>
    param(
        [Parameter(Mandatory = $true)][string]$ArtifactDir,
        [Parameter(Mandatory = $true)][string]$ExpectedSourceSha,
        [Parameter(Mandatory = $true)][string]$ExpectedVersion,
        [Parameter(Mandatory = $true)][string]$EvidencePath,
        [Parameter(Mandatory = $true)][string]$ExpectedCandidateRunId,
        [Parameter(Mandatory = $true)][string]$ExpectedCandidateArtifactName,
        [string]$ExpectedPublisherSubject = '',
        [switch]$RequireSigning
    )
    $failures = [System.Collections.Generic.List[string]]::new()
    $exe = Join-Path $ArtifactDir 'TabDock.exe'
    $manifestPath = Join-Path $ArtifactDir 'release-manifest.json'
    $sumsPath = Join-Path $ArtifactDir 'SHA256SUMS.txt'

    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { $failures.Add("final artifact missing: $exe") }
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { $failures.Add("release manifest missing: $manifestPath") }
    if (-not (Test-Path -LiteralPath $sumsPath -PathType Leaf)) { $failures.Add("SHA256SUMS.txt missing: $sumsPath") }
    if ($failures.Count -gt 0) {
        return [pscustomobject]@{ Eligible = $false; Failures = @($failures) }
    }

    $manifest = $null
    try { $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json }
    catch { $failures.Add("release manifest is malformed JSON: $($_.Exception.Message)") }
    if ($null -eq $manifest) {
        return [pscustomobject]@{ Eligible = $false; Failures = @($failures) }
    }

    if ([string]$manifest.qualificationStatus -ne 'PASS') {
        $failures.Add("manifest qualificationStatus=$($manifest.qualificationStatus) (expected PASS)")
    }
    # --- Release-policy schema contract (P0): the manifest must carry the
    # policy schema generation under which Stage A produced it, and the
    # CURRENT policy only accepts schema >= the minimum. An old candidate
    # (schema absent or lower) is rejected even when everything else looks
    # valid - it must never be evaluated under its own historical policy.
    $schemaCheck = Test-ReleasePolicySchema $manifest
    if (-not $schemaCheck.Valid) {
        $failures.Add($schemaCheck.Failure)
    }
    if (-not [string]::Equals([string]$manifest.sourceCommitSha, $ExpectedSourceSha, [StringComparison]::OrdinalIgnoreCase)) {
        $failures.Add("manifest sourceCommitSha $($manifest.sourceCommitSha) != requested SHA $ExpectedSourceSha")
    }
    if (-not [string]::Equals([string]$manifest.semanticVersion, $ExpectedVersion, [StringComparison]::OrdinalIgnoreCase)) {
        $failures.Add("manifest semanticVersion $($manifest.semanticVersion) != requested version $ExpectedVersion")
    }
    if (-not (Test-SemanticVersion ([string]$manifest.semanticVersion))) {
        $failures.Add("manifest semanticVersion '$($manifest.semanticVersion)' is not a valid semantic version")
    }
    if ([string]$manifest.releaseMode -ne 'PRODUCTION') {
        $failures.Add("manifest releaseMode=$($manifest.releaseMode) (only PRODUCTION candidates from the prepare-release-candidate workflow may be published; qualification-only/RC artifacts are never production releases)")
    }
    $manifestRunId = [string]$manifest.workflowRunId
    if ([string]::IsNullOrWhiteSpace($manifestRunId)) {
        $failures.Add('manifest workflowRunId is absent; the producing workflow run cannot be bound')
    }
    elseif ($manifestRunId -ne $ExpectedCandidateRunId) {
        $failures.Add("manifest workflowRunId $manifestRunId != the Stage A candidate run $ExpectedCandidateRunId (the artifact must come from the exact run that produced it)")
    }
    $buildIdentity = $manifest.buildIdentity
    if ($null -eq $buildIdentity) {
        $failures.Add('manifest buildIdentity is absent; the binary version identity cannot be verified')
    }
    else {
        $biSemantic = [string]$buildIdentity.semanticVersion
        if ([string]::IsNullOrWhiteSpace($biSemantic)) {
            $failures.Add('manifest buildIdentity.semanticVersion is absent')
        }
        elseif ($biSemantic -ne [string]$manifest.semanticVersion) {
            $failures.Add("manifest buildIdentity.semanticVersion $biSemantic != manifest semanticVersion $($manifest.semanticVersion)")
        }
        $biInfo = [string]$buildIdentity.informationalVersion
        if ([string]::IsNullOrWhiteSpace($biInfo)) {
            $failures.Add('manifest buildIdentity.informationalVersion is absent')
        }
        elseif ((Get-SemanticVersionPart $biInfo) -ne [string]$manifest.semanticVersion) {
            $failures.Add("manifest buildIdentity.informationalVersion $biInfo does not carry the manifest semantic version $($manifest.semanticVersion)")
        }
    }

    if ($RequireSigning) {
        # --- Signing provenance classification (P0): a manifest that merely
        # says SIGNED is NOT enough. The provider and the private-key
        # protection class must be APPROVED production policy (non-exportable
        # HSM/cloud key). local-pfx (exportable key), mock, not-configured,
        # and unknown signers never satisfy production, even when the
        # artifact is genuinely Authenticode-signed.
        $provider = [string]$manifest.signingProvider
        if ([string]::IsNullOrWhiteSpace($provider)) {
            $failures.Add('production signing is mandatory but manifest signingProvider is absent; the signer cannot be classified')
        }
        elseif (-not (Test-ApprovedProductionSigningProvider $provider)) {
            $failures.Add("signingProvider '$provider' is not an approved production signing provider (approved: $((Get-ApprovedProductionSigningProviders) -join ', ')); local-PFX, mock, and unconfigured signers never satisfy production policy")
        }
        if ([string]$provider -eq 'mock-test') {
            $failures.Add('signingProvider is the test-only mock provider; mock signing can never be a production release')
        }
        $keyProtection = [string]$manifest.signingKeyProtection
        if ([string]::IsNullOrWhiteSpace($keyProtection)) {
            $failures.Add('production signing is mandatory but manifest signingKeyProtection is absent; private-key protection cannot be classified')
        }
        elseif (-not (Test-ApprovedProductionKeyProtection $keyProtection)) {
            $failures.Add("signingKeyProtection '$keyProtection' is not an approved non-exportable/hardware-backed classification (approved: $((Get-ApprovedProductionKeyProtection) -join ', '))")
        }

        # --- Signing state (existing contract)
        if ([string]$manifest.signingStatus -ne 'SIGNED') {
            $failures.Add("production signing is mandatory but manifest signingStatus=$($manifest.signingStatus)")
        }
        if ([string]$manifest.signatureVerification -ne 'SIGNATURE_VERIFIED') {
            $failures.Add("production signing is mandatory but manifest signatureVerification=$($manifest.signatureVerification)")
        }
        if ([string]::IsNullOrWhiteSpace([string]$manifest.finalSignedSha256)) {
            $failures.Add('production signing is mandatory but manifest finalSignedSha256 is absent')
        }
        if ([string]::Equals([string]$manifest.signingMock, 'true', [StringComparison]::OrdinalIgnoreCase)) {
            $failures.Add('the artifact was produced with test-only mock signing; mock-signed artifacts can never be production releases')
        }

        # --- Certificate identity (P1): the manifest records the signed
        # certificate so the final release evidence has forensic value. The
        # thumbprint is NOT hard-coded (certificates rotate); the recorded
        # identity is required and is cross-checked against the actual bytes
        # below.
        foreach ($identityField in @('signingCertificateSubject', 'signingCertificateThumbprint', 'signingCertificateIssuer', 'signingCertificateValidFrom', 'signingCertificateValidTo')) {
            if ([string]::IsNullOrWhiteSpace([string]$manifest.$identityField)) {
                $failures.Add("production signing is mandatory but manifest $identityField is absent; signed-certificate identity is required provenance")
            }
        }
        $eku = @($manifest.signingCertificateEku | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) })
        if ($eku.Count -eq 0) {
            $failures.Add('production signing is mandatory but manifest signingCertificateEku is absent; the code-signing EKU cannot be confirmed')
        }
        elseif (-not (Test-CertificateEkuIncludesCodeSigning $eku)) {
            $failures.Add("manifest signingCertificateEku does not include the code-signing EKU (1.3.6.1.5.5.7.3.3): $($eku -join ', ')")
        }

        # --- Current trusted publisher policy (P1): the CURRENT policy must
        # independently require the actual publisher. Merely checking
        # actual-cert == manifest-cert is not enough: an artifact and its
        # manifest can consistently record the WRONG publisher. The chain is
        #   CURRENT TRUSTED PUBLISHER POLICY (SIGNING_EXPECTED_SUBJECT from
        #   the trusted policy revision) == manifest signingCertificateSubject
        #   == signed certificate subject on the actual bytes.
        if ([string]::IsNullOrWhiteSpace($ExpectedPublisherSubject)) {
            $failures.Add('current trusted publisher policy is not configured (SIGNING_EXPECTED_SUBJECT); production publication requires the CURRENT expected publisher identity, not merely the publisher recorded in the candidate manifest')
        }
        elseif (-not [string]::Equals([string]$manifest.signingCertificateSubject, $ExpectedPublisherSubject, [StringComparison]::Ordinal)) {
            $failures.Add("manifest signingCertificateSubject '$($manifest.signingCertificateSubject)' != current trusted publisher policy '$ExpectedPublisherSubject'; the candidate records a publisher the CURRENT policy does not approve")
        }

        # --- RFC3161 timestamper identity (P2): the manifest records the
        # timestamper subject/thumbprint so the timestamp provenance has
        # forensic value; absence fails closed and the on-disk timestamper is
        # cross-checked below.
        if ([string]::IsNullOrWhiteSpace([string]$manifest.timestampCertificateSubject)) {
            $failures.Add('production signing is mandatory but manifest timestampCertificateSubject is absent; RFC3161 timestamper identity is required provenance')
        }
        if ([string]::IsNullOrWhiteSpace([string]$manifest.timestampCertificateThumbprint)) {
            $failures.Add('production signing is mandatory but manifest timestampCertificateThumbprint is absent; RFC3161 timestamper identity is required provenance')
        }

        $validFrom = [DateTime]::MinValue
        $validTo = [DateTime]::MinValue
        $fromParsed = [DateTime]::TryParse([string]$manifest.signingCertificateValidFrom, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind, [ref]$validFrom)
        $toParsed = [DateTime]::TryParse([string]$manifest.signingCertificateValidTo, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind, [ref]$validTo)
        if (-not $fromParsed -or -not $toParsed) {
            $failures.Add('manifest signingCertificateValidFrom/signingCertificateValidTo are not parseable dates; the certificate validity window cannot be verified')
        }
        elseif ($validFrom -ge $validTo) {
            $failures.Add("manifest signingCertificateValidFrom $($manifest.signingCertificateValidFrom) is not before signingCertificateValidTo $($manifest.signingCertificateValidTo)")
        }

        # --- Timestamp policy (P1): RFC3161 timestamping is mandatory and the
        # result must be explicitly verified, not assumed from /tr.
        if ([string]$manifest.timestampStatus -ne 'VERIFIED') {
            $failures.Add("production signing is mandatory and RFC3161 timestamping policy requires timestampStatus=VERIFIED, got '$($manifest.timestampStatus)'")
        }

        # --- Independent on-disk verification (P0): the provider reporting
        # success is never sufficient. Windows must independently validate the
        # Authenticode signature, the RFC3161 timestamp, and the certificate
        # identity of the ACTUAL bytes being published.
        $signatureOk = Test-AuthenticodeSignature $exe
        if (-not $signatureOk) {
            $failures.Add('production signing is mandatory but the final executable is not Authenticode-verified on disk (signtool verify /pa)')
        }
        else {
            $certInfo = Get-SignerCertificateInfo $exe
            if ($null -eq $certInfo) {
                $failures.Add('production signing is mandatory but no valid signed certificate could be read from the final executable')
            }
            else {
                if (-not (Test-CertificateEkuIncludesCodeSigning $certInfo.Eku)) {
                    $failures.Add("the signed certificate on the final executable does not include the code-signing EKU (1.3.6.1.5.5.7.3.3): $($certInfo.Eku -join ', ')")
                }
                if (-not [string]::IsNullOrWhiteSpace([string]$manifest.signingCertificateThumbprint) -and
                    $certInfo.Thumbprint -ne [string]$manifest.signingCertificateThumbprint) {
                    $failures.Add("signed certificate thumbprint on disk $($certInfo.Thumbprint) != manifest signingCertificateThumbprint $($manifest.signingCertificateThumbprint)")
                }
                if (-not [string]::IsNullOrWhiteSpace([string]$manifest.signingCertificateSubject) -and
                    $certInfo.Subject -ne [string]$manifest.signingCertificateSubject) {
                    $failures.Add("signed certificate subject on disk '$($certInfo.Subject)' != manifest signingCertificateSubject '$($manifest.signingCertificateSubject)'")
                }
                # CURRENT trusted publisher policy must equal the ACTUAL bytes
                # (not merely equal the manifest): an artifact whose manifest
                # consistently records the wrong publisher still fails.
                if (-not [string]::IsNullOrWhiteSpace($ExpectedPublisherSubject) -and
                    $certInfo.Subject -ne $ExpectedPublisherSubject) {
                    $failures.Add("signed certificate subject on disk '$($certInfo.Subject)' != current trusted publisher policy '$ExpectedPublisherSubject'; the ACTUAL publisher does not satisfy the CURRENT policy")
                }
                # RFC3161 timestamper identity on disk must equal the manifest
                # record (and must exist - Test-AuthenticodeTimestamp already
                # fails when no timestamp certificate is present).
                if ([string]::IsNullOrWhiteSpace([string]$certInfo.TimestamperSubject)) {
                    $failures.Add('no RFC3161 timestamper subject could be read from the final executable; timestamp provenance cannot be bound')
                }
                elseif (-not [string]::IsNullOrWhiteSpace([string]$manifest.timestampCertificateSubject) -and
                    $certInfo.TimestamperSubject -ne [string]$manifest.timestampCertificateSubject) {
                    $failures.Add("timestamper subject on disk '$($certInfo.TimestamperSubject)' != manifest timestampCertificateSubject '$($manifest.timestampCertificateSubject)'")
                }
                if ([string]::IsNullOrWhiteSpace([string]$certInfo.TimestamperThumbprint)) {
                    $failures.Add('no RFC3161 timestamper thumbprint could be read from the final executable; timestamp provenance cannot be bound')
                }
                elseif (-not [string]::IsNullOrWhiteSpace([string]$manifest.timestampCertificateThumbprint) -and
                    $certInfo.TimestamperThumbprint -ne [string]$manifest.timestampCertificateThumbprint) {
                    $failures.Add("timestamper thumbprint on disk '$($certInfo.TimestamperThumbprint)' != manifest timestampCertificateThumbprint '$($manifest.timestampCertificateThumbprint)'")
                }
            }
            $timestampOk = Test-AuthenticodeTimestamp $exe
            if (-not $timestampOk) {
                $failures.Add('production signing is mandatory but the RFC3161 timestamp could not be verified on the final executable (timestamp certificate absent or signtool verification failed)')
            }
        }
    }

    $finalHash = $null
    try {
        $finalHash = Get-FinalArtifactSha256 $manifest
    }
    catch { $failures.Add($_.Exception.Message) }

    try {
        $actual = Assert-ChecksumsMatchArtifact $exe $sumsPath
        if ($null -ne $finalHash -and $actual -ne $finalHash) {
            $failures.Add("final artifact SHA-256 $actual != manifest final hash $finalHash")
        }
        if ($actual -ne [string]$manifest.artifactSha256) {
            $failures.Add("final artifact SHA-256 $actual != manifest artifactSha256 $($manifest.artifactSha256)")
        }
    }
    catch { $failures.Add($_.Exception.Message) }

    if ($null -eq $finalHash) {
        $failures.Add('cannot verify external evidence without a final artifact hash')
    }
    else {
        $evidenceResult = Test-ExternalEvidenceFile $EvidencePath $ExpectedSourceSha $finalHash `
            -ExpectedCandidateRunId $ExpectedCandidateRunId `
            -ExpectedCandidateArtifactName $ExpectedCandidateArtifactName `
            -QualificationBundleRoot $ArtifactDir -RequireQualificationBundle
        if (-not $evidenceResult.Valid) {
            foreach ($failure in $evidenceResult.Failures) {
                $failures.Add([string]$failure)
            }
        }
    }

    return [pscustomobject]@{ Eligible = $failures.Count -eq 0; Failures = @($failures) }
}

# Qualification bundles are part of the trusted release-tooling policy
# surface. The helper is data-only and never executes a candidate or returned
# evidence.
$qualificationBundleModule = Join-Path $PSScriptRoot 'qualification-bundle.ps1'
if (Test-Path -LiteralPath $qualificationBundleModule -PathType Leaf) {
    . $qualificationBundleModule
}
$qualificationPackageModule = Join-Path $PSScriptRoot 'qualification-package.ps1'
if (Test-Path -LiteralPath $qualificationPackageModule -PathType Leaf) {
    . $qualificationPackageModule
}
