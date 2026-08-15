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
    and the signtool discovery / Authenticode verification helpers shared
    with scripts/sign-release.ps1.

    Nothing in this file reads signing material, performs signing, or creates
    a GitHub Release; it is safe to run anywhere, including tests.

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
        final artifact hash, and (schema v2) to the exact Stage A workflow run
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
        [string]$ExpectedCandidateArtifactName = ''
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

    if ($evidence.schemaVersion -ne 2) {
        $failures.Add("external evidence schemaVersion=$($evidence.schemaVersion) (expected 2)")
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
        }
    }
    return [pscustomobject]@{ Valid = $failures.Count -eq 0; Failures = @($failures); Evidence = $evidence }
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
        signature by running `signtool verify /pa /v` (no signing material is
        needed to verify). Fail-closed: returns $false when signtool is
        unavailable or verification fails. Verification is never faked.
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
    & $signtool @('verify', '/pa', '/v', $ExePath) 2>&1 | Out-Null
    $ok = $LASTEXITCODE -eq 0
    if (-not $ok) {
        Write-Host "Test-AuthenticodeSignature: signtool verify /pa failed (exit $LASTEXITCODE) for $ExePath" -ForegroundColor Red
    }
    return $ok
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
          - external evidence (schema v2) valid, source SHA == requested SHA,
            artifact SHA == final distributed artifact hash, candidate
            workflow run + artifact == the exact Stage A run and artifact
            being published, finalWindowsHumanSmoke == PASS,
            physicalMixedDpi == PASS, windowsCompatibility == PASS for
            Windows 10 and Windows 11
          - when production signing is mandatory: signingStatus == SIGNED,
            signatureVerification == SIGNATURE_VERIFIED,
            finalSignedSha256 == final artifact hash, the artifact is NOT a
            test-only mock-signed artifact, and `signtool verify /pa`
            independently confirms the Authenticode signature on disk
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
        $signatureOk = Test-AuthenticodeSignature $exe
        if (-not $signatureOk) {
            $failures.Add('production signing is mandatory but the final executable is not Authenticode-verified on disk (signtool verify /pa)')
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
            -ExpectedCandidateArtifactName $ExpectedCandidateArtifactName
        if (-not $evidenceResult.Valid) {
            foreach ($failure in $evidenceResult.Failures) {
                $failures.Add([string]$failure)
            }
        }
    }

    return [pscustomobject]@{ Eligible = $failures.Count -eq 0; Failures = @($failures) }
}
