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

function Test-ExternalEvidenceFile {
    <#
    .SYNOPSIS
        Validates a production qualification evidence file
        (release-external-evidence.json) against the exact candidate SHA and
        the final distributed artifact hash.

    .DESCRIPTION
        The evidence file is the auditable record that the mandatory external
        gates (final human Windows smoke, physical mixed-DPI qualification)
        were performed against the exact bytes being published. It is bound to
        the candidate source SHA and to the final artifact hash so evidence
        from another candidate or another artifact can never be reused.

        A caller-controlled boolean is NOT accepted as evidence: only a
        schema-validated record with per-gate PASS status, operator,
        completion time, and evidence detail passes. Anything else (missing
        file, malformed JSON, wrong schema version, wrong SHA, wrong artifact
        hash, FAIL, BLOCKED_EXTERNAL, missing fields) fails closed.

        Returns [pscustomobject]@{ Valid=[bool]; Failures=[string[]]; Evidence=$parsed }.
    #>
    param(
        [Parameter(Mandatory = $true)][string]$EvidencePath,
        [Parameter(Mandatory = $true)][string]$ExpectedSourceSha,
        [Parameter(Mandatory = $true)][string]$ExpectedArtifactSha
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

    if ($evidence.schemaVersion -ne 1) {
        $failures.Add("external evidence schemaVersion=$($evidence.schemaVersion) (expected 1)")
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
        if ([string]::IsNullOrWhiteSpace([string]$g.completedAt)) {
            $failures.Add("external gate '$gate' is missing 'completedAt'")
        }
        if ([string]::IsNullOrWhiteSpace([string]$g.evidence)) {
            $failures.Add("external gate '$gate' is missing 'evidence'")
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
          - manifest semanticVersion == requested version
          - actual file hash == manifest.artifactSha256
          - SHA256SUMS.txt == actual file hash (triple consistency)
          - external evidence valid, source SHA == requested SHA,
            artifact SHA == final distributed artifact hash,
            finalWindowsHumanSmoke == PASS, physicalMixedDpi == PASS
          - when production signing is mandatory: signingStatus == SIGNED,
            signatureVerification == SIGNATURE_VERIFIED,
            finalSignedSha256 == final artifact hash, the artifact is NOT a
            test-only mock-signed artifact, and `signtool verify /pa`
            independently confirms the Authenticode signature on disk
        Any failure is returned (never thrown) as a Failures list; the caller
        must refuse to publish when Eligible is $false.

        Returns [pscustomobject]@{ Eligible=[bool]; Failures=[string[]] }.
    #>
    param(
        [Parameter(Mandatory = $true)][string]$ArtifactDir,
        [Parameter(Mandatory = $true)][string]$ExpectedSourceSha,
        [Parameter(Mandatory = $true)][string]$ExpectedVersion,
        [Parameter(Mandatory = $true)][string]$EvidencePath,
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
        $evidenceResult = Test-ExternalEvidenceFile $EvidencePath $ExpectedSourceSha $finalHash
        if (-not $evidenceResult.Valid) {
            foreach ($failure in $evidenceResult.Failures) {
                $failures.Add([string]$failure)
            }
        }
    }

    return [pscustomobject]@{ Eligible = $failures.Count -eq 0; Failures = @($failures) }
}
