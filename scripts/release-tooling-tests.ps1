<#
.SYNOPSIS
    Deterministic regression tests for the TabDock release tooling.

.DESCRIPTION
    Proves the release-chain semantics WITHOUT real signing credentials and
    WITHOUT publishing anything:

      A. unsigned path: SHA256SUMS.txt describes the actual final executable.
      B. signed/mutated path: the executable changes at the signing stage, the
         final hash is recomputed, artifactSha256 and SHA256SUMS.txt use the
         final hash, and unsignedQualifiedSha256 retains the original hash.
      C. publication provenance: the publication gate verifies the FINAL
         distributed hash (file == manifest == SHA256SUMS.txt) plus the
         external evidence record.
      D. signing failure can never produce a production-qualified result.
      E. signature-verification failure can never be treated as a valid
         signed production artifact (Authenticode verification is never faked;
         the gate re-runs signtool verify /pa and unsigned test artifacts
         therefore fail it).

    Adversarial coverage includes: missing/malformed external evidence,
    evidence from the wrong source SHA or artifact hash, smoke FAIL /
    BLOCKED_EXTERNAL, mixed-DPI FAIL / BLOCKED_EXTERNAL, unsigned artifact
    under mandatory signing, signed-artifact checksum mismatch, manifest hash
    mismatch, SHA256SUMS mismatch, tampered final executable, wrong semantic
    version, wrong candidate SHA, and a dirty local release candidate.

    The mock signer (scripts/sign-release.ps1 -MockSign*) models the real
    fact that Authenticode signing changes the artifact bytes. It performs NO
    Authenticode operation, is refused when real material is present, is
    recorded as Mock=true, and can never pass the production gate.

    This script NEVER creates a GitHub Release, never contacts the network,
    and never touches signing material.

.EXAMPLE
    pwsh ./scripts/release-tooling-tests.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$modulePath = Join-Path $PSScriptRoot 'release-tooling.ps1'
$signScript = Join-Path $PSScriptRoot 'sign-release.ps1'
$qualifyScript = Join-Path $PSScriptRoot 'release-qualify.ps1'
. $modulePath

$testRoot = Join-Path ([IO.Path]::GetTempPath()) "TabDock-release-tooling-tests-$PID-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$passCount = 0
$failureList = [System.Collections.Generic.List[string]]::new()

function New-TestCase {
    param([string]$Name, [scriptblock]$Body)
    try {
        & $Body
        $script:passCount++
        Write-Host "  PASS  $Name" -ForegroundColor Green
    }
    catch {
        $script:failureList.Add("$Name : $($_.Exception.Message)")
        Write-Host "  FAIL  $Name : $($_.Exception.Message)" -ForegroundColor Red
    }
}

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Assert-Throws {
    param([string]$Message, [scriptblock]$Body)
    $threw = $false
    try { & $Body } catch { $threw = $true }
    if (-not $threw) { throw $Message }
}

function New-DummyArtifact {
    param([string]$Path, [int]$Size = 4096)
    $bytes = [byte[]]::new($Size)
    for ($i = 0; $i -lt $Size; $i++) { $bytes[$i] = [byte](($i * 31 + 7) % 256) }
    [IO.File]::WriteAllBytes($Path, $bytes)
}

function New-TestManifest {
    param(
        [string]$SourceSha = ('a' * 40),
        [string]$Version = '1.0.0',
        [string]$UnsignedSha = '',
        [string]$FinalSignedSha = '',
        [string]$SigningStatus = 'NOT_CONFIGURED',
        [string]$SignatureVerification = 'NOT_PERFORMED',
        [string]$QualificationStatus = 'PASS',
        [bool]$Mock = $false
    )
    return [ordered]@{
        product                = 'TabDock'
        semanticVersion        = $Version
        sourceCommitSha        = $SourceSha
        artifactFileName       = 'TabDock.exe'
        artifactSha256         = 'unavailable'
        unsignedQualifiedSha256 = $UnsignedSha
        finalSignedSha256      = if ($FinalSignedSha) { $FinalSignedSha } else { $null }
        signingStatus          = $SigningStatus
        signatureVerification  = $SignatureVerification
        signingMock            = if ($Mock) { $true } else { $null }
        qualificationStatus    = $QualificationStatus
        productionReleaseEligibility = 'BLOCKED_EXTERNAL'
    }
}

function New-SyntheticArtifactDir {
    <#
    .SYNOPSIS
        Creates a finalized artifact directory (dummy TabDock.exe +
        release-manifest.json + SHA256SUMS.txt via Complete-ReleaseRecords).
        The manifest on disk is unsigned; tests mutate it as needed.
    #>
    param(
        [string]$Parent,
        [string]$Name,
        [string]$SourceSha = ('a' * 40),
        [string]$Version = '1.0.0'
    )
    $dir = Join-Path $Parent $Name
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    $exe = Join-Path $dir 'TabDock.exe'
    New-DummyArtifact $exe
    $manifest = New-TestManifest -SourceSha $SourceSha -Version $Version -UnsignedSha (Get-FileSha256Lower $exe)
    $records = Complete-ReleaseRecords -Manifest $manifest -ArtifactPath $exe -OutDir $dir
    return [pscustomobject]@{
        Dir            = $dir
        Exe            = $exe
        ManifestPath   = $records.ManifestPath
        SumsPath       = $records.SumsPath
        ArtifactSha256 = $records.ArtifactSha256
    }
}

function Update-TestManifest {
    param([string]$ManifestPath, [hashtable]$Values)
    $m = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
    foreach ($key in $Values.Keys) {
        $m.$key = $Values[$key]
    }
    $m | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $ManifestPath -Encoding utf8
}

function New-TestEvidence {
    param(
        [string]$SourceSha = ('a' * 40),
        [string]$ArtifactSha = ('b' * 64)
    )
    return [ordered]@{
        schemaVersion      = 1
        sourceCommitSha    = $SourceSha
        artifactSha256     = $ArtifactSha
        finalWindowsHumanSmoke = [ordered]@{
            status      = 'PASS'
            completedAt = '2026-08-15T00:00:00Z'
            operator    = 'Test Operator'
            evidence    = 'manual smoke executed against the exact artifact'
        }
        physicalMixedDpi = [ordered]@{
            status      = 'PASS'
            completedAt = '2026-08-15T00:00:00Z'
            operator    = 'Test Operator'
            evidence    = 'mixed-DPI qualification executed against the exact artifact'
        }
    }
}

function Save-TestEvidence {
    param([string]$Path, $Evidence)
    $Evidence | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $Path -Encoding utf8
}

try {
    Write-Host ''
    Write-Host '==> A. Unsigned path: checksums describe the final artifact' -ForegroundColor Cyan

    New-TestCase 'unsigned-path-sha256sums-describes-final-artifact' {
        $dir = Join-Path $testRoot 't-unsigned'
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        $exe = Join-Path $dir 'TabDock.exe'
        New-DummyArtifact $exe
        $hash = Get-FileSha256Lower $exe
        $manifest = New-TestManifest -UnsignedSha $hash
        $records = Complete-ReleaseRecords -Manifest $manifest -ArtifactPath $exe -OutDir $dir
        $sums = Read-Sha256Sums $records.SumsPath
        Assert-True ($records.ArtifactSha256 -eq $hash) 'finalized hash differs from the actual file hash'
        Assert-True ($sums.Hash -eq $hash) 'SHA256SUMS.txt differs from the actual final executable hash'
        Assert-True ([string]$manifest.artifactSha256 -eq $hash) 'manifest artifactSha256 differs from the actual final executable hash'
        Assert-True ($manifest.finalSignedSha256 -eq $null) 'unsigned path must not record a signed hash'
    }

    New-TestCase 'qualification-only-records-need-no-evidence' {
        # Qualification-only runs never invoke the publication gate and are
        # valid with external gates blocked; this asserts the module supports
        # the unsigned finalization that qualification-only runs rely on and
        # that Get-FinalArtifactSha256 selects the unsigned hash.
        $dir = Join-Path $testRoot 't-qual-only'
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        $exe = Join-Path $dir 'TabDock.exe'
        New-DummyArtifact $exe
        $hash = Get-FileSha256Lower $exe
        $manifest = New-TestManifest -UnsignedSha $hash
        $null = Complete-ReleaseRecords -Manifest $manifest -ArtifactPath $exe -OutDir $dir
        $final = Get-FinalArtifactSha256 $manifest
        Assert-True ($final -eq $hash) 'final hash must be the unsigned qualified hash when nothing was signed'
    }

    Write-Host ''
    Write-Host '==> B. Signed/mutated path: final hash recomputed, provenance retained' -ForegroundColor Cyan

    New-TestCase 'signed-path-recomputes-final-hash-and-retains-unsigned-provenance' {
        $dir = Join-Path $testRoot 't-signed'
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        $exe = Join-Path $dir 'TabDock.exe'
        New-DummyArtifact $exe
        $unsignedHash = Get-FileSha256Lower $exe

        # Model Authenticode signing: the artifact bytes change.
        $original = [IO.File]::ReadAllBytes($exe)
        $mutated = [byte[]]::new($original.Length + 32)
        [Array]::Copy($original, $mutated, $original.Length)
        [IO.File]::WriteAllBytes($exe, $mutated)
        $finalHash = Get-FileSha256Lower $exe
        Assert-True ($finalHash -ne $unsignedHash) 'test premise: signing mutation must change the hash'

        $manifest = New-TestManifest -UnsignedSha $unsignedHash -FinalSignedSha $finalHash `
            -SigningStatus 'SIGNED' -SignatureVerification 'SIGNATURE_VERIFIED'
        $records = Complete-ReleaseRecords -Manifest $manifest -ArtifactPath $exe -OutDir $dir
        $sums = Read-Sha256Sums $records.SumsPath
        Assert-True ($records.ArtifactSha256 -eq $finalHash) 'artifactSha256 must be the FINAL (post-sign) hash'
        Assert-True ($sums.Hash -eq $finalHash) 'SHA256SUMS.txt must describe the FINAL signed executable'
        Assert-True ([string]$manifest.unsignedQualifiedSha256 -eq $unsignedHash) 'unsigned provenance hash must be retained'
        Assert-True ([string]$manifest.artifactSha256 -eq $finalHash) 'manifest artifactSha256 must be the FINAL hash'
        Assert-True ([string]$manifest.finalSignedSha256 -eq $finalHash) 'finalSignedSha256 must equal the final hash'
    }

    New-TestCase 'signed-artifact-checksum-mismatch-fails-closed' {
        $dir = Join-Path $testRoot 't-signed-mismatch'
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        $exe = Join-Path $dir 'TabDock.exe'
        New-DummyArtifact $exe
        $unsignedHash = Get-FileSha256Lower $exe
        $wrongFinal = ('c' * 64)
        $manifest = New-TestManifest -UnsignedSha $unsignedHash -FinalSignedSha $wrongFinal `
            -SigningStatus 'SIGNED' -SignatureVerification 'SIGNATURE_VERIFIED'
        Assert-Throws 'finalized records must fail when finalSignedSha256 does not describe the on-disk bytes' {
            $null = Complete-ReleaseRecords -Manifest $manifest -ArtifactPath $exe -OutDir $dir
        }
    }

    New-TestCase 'unsigned-artifact-tampered-after-qualification-fails-closed' {
        $dir = Join-Path $testRoot 't-tampered'
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        $exe = Join-Path $dir 'TabDock.exe'
        New-DummyArtifact $exe
        $hash = Get-FileSha256Lower $exe
        $manifest = New-TestManifest -UnsignedSha $hash
        $original = [IO.File]::ReadAllBytes($exe)
        $mutated = [byte[]]::new($original.Length + 4)
        [Array]::Copy($original, $mutated, $original.Length)
        [IO.File]::WriteAllBytes($exe, $mutated)
        Assert-Throws 'finalization must fail when the unsigned artifact changed after qualification' {
            $null = Complete-ReleaseRecords -Manifest $manifest -ArtifactPath $exe -OutDir $dir
        }
    }

    New-TestCase 'mock-signer-changes-bytes-and-reports-final-hash' {
        $exe = Join-Path $testRoot 't-mock-sign.exe'
        New-DummyArtifact $exe
        $preHash = Get-FileSha256Lower $exe
        $output = & $signScript -ExePath $exe -MockSign 2>&1 | Out-String
        Assert-True ($LASTEXITCODE -eq 0) "mock signer must exit 0, got $LASTEXITCODE`n$output"
        $postHash = Get-FileSha256Lower $exe
        Assert-True ($postHash -ne $preHash) 'mock signing must change the artifact bytes like real signing does'
        $result = $output | ConvertFrom-Json
        Assert-True ($result.Status -eq 'SIGNED') 'mock signer must report SIGNED'
        Assert-True ($result.Verification -eq 'SIGNATURE_VERIFIED') 'mock signer must report SIGNATURE_VERIFIED'
        Assert-True ($result.Mock -eq $true) 'mock signer must mark the result Mock=true'
        Assert-True ($result.FinalSha256 -eq $postHash) 'mock signer FinalSha256 must be the actual post-signature hash'
    }

    New-TestCase 'mock-sign-then-finalize-records-end-to-end' {
        # The integration seam between sign-release.ps1's output contract and
        # the record finalization: the returned FinalSha256 drives the final
        # hash everywhere.
        $exe = Join-Path $testRoot 't-mock-sign-finalize.exe'
        New-DummyArtifact $exe
        $unsignedHash = Get-FileSha256Lower $exe
        $output = & $signScript -ExePath $exe -MockSign 2>&1 | Out-String
        Assert-True ($LASTEXITCODE -eq 0) "mock signer failed: $output"
        $result = $output | ConvertFrom-Json
        $dir = Join-Path $testRoot 't-mock-sign-finalize'
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        $manifest = New-TestManifest -UnsignedSha $unsignedHash -FinalSignedSha $result.FinalSha256 `
            -SigningStatus 'SIGNED' -SignatureVerification 'SIGNATURE_VERIFIED' -Mock $true
        $records = Complete-ReleaseRecords -Manifest $manifest -ArtifactPath $exe -OutDir $dir
        $sums = Read-Sha256Sums $records.SumsPath
        Assert-True ($records.ArtifactSha256 -eq $result.FinalSha256) 'records must finalize on the post-sign hash'
        Assert-True ($sums.Hash -eq $result.FinalSha256) 'SHA256SUMS.txt must carry the post-sign hash'
        Assert-True ([string]$manifest.unsignedQualifiedSha256 -eq $unsignedHash) 'unsigned provenance must survive the signed path'
    }

    Write-Host ''
    Write-Host '==> D/E. Signing failure and verification failure semantics' -ForegroundColor Cyan

    New-TestCase 'mock-sign-failure-exits-nonzero-without-touching-the-file' {
        $exe = Join-Path $testRoot 't-mock-sign-failure.exe'
        New-DummyArtifact $exe
        $preHash = Get-FileSha256Lower $exe
        $output = & $signScript -ExePath $exe -MockSignFailure 2>&1 | Out-String
        Assert-True ($LASTEXITCODE -eq 3) "mock sign failure must exit 3, got $LASTEXITCODE"
        $result = $output | ConvertFrom-Json
        Assert-True ($result.Status -eq 'SIGNING_FAILED') 'mock sign failure must report SIGNING_FAILED'
        Assert-True ((Get-FileSha256Lower $exe) -eq $preHash) 'a failed sign must not mutate the artifact'
    }

    New-TestCase 'mock-verify-failure-exits-nonzero' {
        $exe = Join-Path $testRoot 't-mock-verify-failure.exe'
        New-DummyArtifact $exe
        $output = & $signScript -ExePath $exe -MockVerifyFailure 2>&1 | Out-String
        Assert-True ($LASTEXITCODE -eq 3) "mock verify failure must exit 3, got $LASTEXITCODE"
        $result = $output | ConvertFrom-Json
        Assert-True ($result.Status -eq 'SIGNED' -and $result.Verification -eq 'FAILED') 'mock verify failure must report SIGNED/FAILED'
    }

    New-TestCase 'mock-modes-refuse-to-mix-with-real-material' {
        $exe = Join-Path $testRoot 't-mock-real.exe'
        New-DummyArtifact $exe
        $env:SIGNCERT_BASE64 = 'c2hvdWxkLW5ldmVyLWJlLXVzZWQ='
        try {
            $refused = $false
            try {
                & $signScript -ExePath $exe -MockSign 2>&1 | Out-Null
            }
            catch {
                $refused = $true
            }
            Assert-True $refused 'mock mode must refuse to run while real material is present'
        }
        finally {
            Remove-Item Env:\SIGNCERT_BASE64 -ErrorAction SilentlyContinue
        }
    }

    New-TestCase 'authenticode-verification-is-never-faked' {
        # An unsigned test artifact must NEVER pass the independent
        # Authenticode re-verification used by the production gate.
        $exe = Join-Path $testRoot 't-unsigned-verify.exe'
        New-DummyArtifact $exe
        Assert-True (-not (Test-AuthenticodeSignature $exe)) 'unsigned artifacts must fail Authenticode verification (fail closed)'
    }

    Write-Host ''
    Write-Host '==> Publication gate: adversarial cases' -ForegroundColor Cyan

    $goodSha = 'a' * 40
    $goodVersion = '1.0.0'

    New-TestCase 'unsigned-artifact-rejected-when-production-signing-mandatory' {
        $art = New-SyntheticArtifactDir $testRoot 'g-unsigned-mandatory' -SourceSha $goodSha -Version $goodVersion
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -RequireSigning
        Assert-True (-not $gate.Eligible) 'unsigned artifact must be ineligible under mandatory production signing'
        Assert-True (($gate.Failures -join ';') -match 'signing') 'failure must identify the signing policy'
    }

    New-TestCase 'manifest-signing-failure-state-rejected' {
        $art = New-SyntheticArtifactDir $testRoot 'g-signing-failed' -SourceSha $goodSha -Version $goodVersion
        Update-TestManifest $art.ManifestPath @{ signingStatus = 'SIGNING_FAILED'; signatureVerification = 'FAILED' }
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -RequireSigning
        Assert-True (-not $gate.Eligible) 'a SIGNING_FAILED manifest must never be production-eligible'
    }

    New-TestCase 'signature-verification-failure-rejected' {
        $art = New-SyntheticArtifactDir $testRoot 'g-verify-failed' -SourceSha $goodSha -Version $goodVersion
        Update-TestManifest $art.ManifestPath @{ signingStatus = 'SIGNED'; signatureVerification = 'FAILED'; finalSignedSha256 = $art.ArtifactSha256 }
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -RequireSigning
        Assert-True (-not $gate.Eligible) 'an unverified signature must never be treated as valid for production'
        Assert-True (($gate.Failures -join ';') -match 'signatureVerification') 'failure must cite signature verification'
    }

    New-TestCase 'mock-signed-artifact-rejected-for-production' {
        $art = New-SyntheticArtifactDir $testRoot 'g-mock' -SourceSha $goodSha -Version $goodVersion
        Update-TestManifest $art.ManifestPath @{ signingStatus = 'SIGNED'; signatureVerification = 'SIGNATURE_VERIFIED'; finalSignedSha256 = $art.ArtifactSha256; signingMock = $true }
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -RequireSigning
        Assert-True (-not $gate.Eligible) 'a test-only mock-signed artifact must never be production-eligible'
        Assert-True (($gate.Failures -join ';') -match 'mock') 'failure must identify mock signing'
    }

    New-TestCase 'missing-external-evidence-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'g-missing-evidence' -SourceSha $goodSha -Version $goodVersion
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath (Join-Path $art.Dir 'does-not-exist.json')
        Assert-True (-not $gate.Eligible) 'missing external evidence must fail closed'
    }

    New-TestCase 'malformed-external-evidence-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'g-malformed-evidence' -SourceSha $goodSha -Version $goodVersion
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        [IO.File]::WriteAllText($evidencePath, '{ not json', [Text.UTF8Encoding]::new($false))
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath
        Assert-True (-not $gate.Eligible) 'malformed external evidence must fail closed'
    }

    New-TestCase 'evidence-from-wrong-source-sha-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'g-wrong-sha' -SourceSha $goodSha -Version $goodVersion
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha ('d' * 40) -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath
        Assert-True (-not $gate.Eligible) 'evidence bound to another source SHA must fail closed'
        Assert-True (($gate.Failures -join ';') -match 'sourceCommitSha') 'failure must cite the SHA binding'
    }

    New-TestCase 'evidence-from-wrong-artifact-hash-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'g-wrong-hash' -SourceSha $goodSha -Version $goodVersion
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha ('e' * 64))
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath
        Assert-True (-not $gate.Eligible) 'evidence bound to another artifact must fail closed'
        Assert-True (($gate.Failures -join ';') -match 'artifactSha256') 'failure must cite the artifact binding'
    }

    New-TestCase 'smoke-fail-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'g-smoke-fail' -SourceSha $goodSha -Version $goodVersion
        $evidence = New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256
        $evidence.finalWindowsHumanSmoke.status = 'FAIL'
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath $evidence
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath
        Assert-True (-not $gate.Eligible) 'finalWindowsHumanSmoke=FAIL must fail closed'
    }

    New-TestCase 'smoke-blocked-external-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'g-smoke-blocked' -SourceSha $goodSha -Version $goodVersion
        $evidence = New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256
        $evidence.finalWindowsHumanSmoke.status = 'BLOCKED_EXTERNAL'
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath $evidence
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath
        Assert-True (-not $gate.Eligible) 'finalWindowsHumanSmoke=BLOCKED_EXTERNAL must fail closed'
    }

    New-TestCase 'dpi-fail-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'g-dpi-fail' -SourceSha $goodSha -Version $goodVersion
        $evidence = New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256
        $evidence.physicalMixedDpi.status = 'FAIL'
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath $evidence
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath
        Assert-True (-not $gate.Eligible) 'physicalMixedDpi=FAIL must fail closed'
    }

    New-TestCase 'dpi-blocked-external-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'g-dpi-blocked' -SourceSha $goodSha -Version $goodVersion
        $evidence = New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256
        $evidence.physicalMixedDpi.status = 'BLOCKED_NO_MIXED_DPI_HARDWARE'
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath $evidence
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath
        Assert-True (-not $gate.Eligible) 'physicalMixedDpi=BLOCKED_NO_MIXED_DPI_HARDWARE must fail closed for production'
    }

    New-TestCase 'evidence-missing-gate-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'g-missing-gate' -SourceSha $goodSha -Version $goodVersion
        $evidence = New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256
        $evidence.Remove('physicalMixedDpi')
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath $evidence
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath
        Assert-True (-not $gate.Eligible) 'evidence without a mandatory gate must fail closed'
    }

    New-TestCase 'evidence-missing-operator-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'g-no-operator' -SourceSha $goodSha -Version $goodVersion
        $evidence = New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256
        $evidence.finalWindowsHumanSmoke.operator = ''
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath $evidence
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath
        Assert-True (-not $gate.Eligible) 'evidence without an operator must fail closed'
    }

    New-TestCase 'evidence-wrong-schema-version-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'g-schema' -SourceSha $goodSha -Version $goodVersion
        $evidence = New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256
        $evidence.schemaVersion = 99
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath $evidence
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath
        Assert-True (-not $gate.Eligible) 'wrong evidence schema version must fail closed'
    }

    New-TestCase 'manifest-hash-mismatch-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'g-manifest-hash' -SourceSha $goodSha -Version $goodVersion
        Update-TestManifest $art.ManifestPath @{ artifactSha256 = ('f' * 64) }
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath
        Assert-True (-not $gate.Eligible) 'manifest hash disagreement must fail closed'
    }

    New-TestCase 'sha256sums-mismatch-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'g-sums' -SourceSha $goodSha -Version $goodVersion
        [IO.File]::WriteAllText($art.SumsPath, ('0' * 64) + "  TabDock.exe`r`n", [Text.UTF8Encoding]::new($false))
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath
        Assert-True (-not $gate.Eligible) 'SHA256SUMS disagreement must fail closed'
    }

    New-TestCase 'final-executable-tampered-after-qualification-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'g-tampered' -SourceSha $goodSha -Version $goodVersion
        $bytes = [IO.File]::ReadAllBytes($art.Exe)
        $bytes[0] = $bytes[0] -bxor 0xFF
        [IO.File]::WriteAllBytes($art.Exe, $bytes)
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath
        Assert-True (-not $gate.Eligible) 'a tampered final executable must fail closed'
    }

    New-TestCase 'wrong-semantic-version-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'g-version' -SourceSha $goodSha -Version $goodVersion
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion '9.9.9' -EvidencePath $evidencePath
        Assert-True (-not $gate.Eligible) 'a version disagreement must fail closed'
    }

    New-TestCase 'wrong-candidate-sha-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'g-sha' -SourceSha $goodSha -Version $goodVersion
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha ('c' * 40) -ExpectedVersion $goodVersion -EvidencePath $evidencePath
        Assert-True (-not $gate.Eligible) 'a requested-SHA disagreement must fail closed'
    }

    New-TestCase 'failed-qualification-manifest-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'g-qual-failed' -SourceSha $goodSha -Version $goodVersion
        Update-TestManifest $art.ManifestPath @{ qualificationStatus = 'FAIL' }
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath
        Assert-True (-not $gate.Eligible) 'a FAIL manifest must fail closed'
    }

    Write-Host ''
    Write-Host '==> Publication gate: happy paths' -ForegroundColor Cyan

    New-TestCase 'production-eligible-synthetic-happy-path' {
        $art = New-SyntheticArtifactDir $testRoot 'h-production' -SourceSha $goodSha -Version $goodVersion
        Update-TestManifest $art.ManifestPath @{ signingStatus = 'SIGNED'; signatureVerification = 'SIGNATURE_VERIFIED'; finalSignedSha256 = $art.ArtifactSha256 }
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        # The synthetic artifact is not really Authenticode-signed, so the
        # independent signtool re-verification is exercised as the signing
        # branch; policy-declared signing is covered by the negative cases.
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath
        Assert-True ($gate.Eligible) "production-eligible synthetic records must pass all non-signing gates: $($gate.Failures -join '; ')"
        Assert-True ($gate.Failures.Count -eq 0) 'no failures expected on the synthetic happy path'
    }

    New-TestCase 'mandatory-signing-adds-exactly-the-signing-failure' {
        # Decomposition: with RequireSigning, the ONLY added failure must be
        # the independent Authenticode verification (the test artifact is
        # intentionally unsigned). This proves the gate does not hide other
        # defects behind the signing branch.
        $art = New-SyntheticArtifactDir $testRoot 'h-mandatory' -SourceSha $goodSha -Version $goodVersion
        Update-TestManifest $art.ManifestPath @{ signingStatus = 'SIGNED'; signatureVerification = 'SIGNATURE_VERIFIED'; finalSignedSha256 = $art.ArtifactSha256 }
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -RequireSigning
        Assert-True (-not $gate.Eligible) 'unsigned synthetic artifact must fail mandatory signing'
        Assert-True ($gate.Failures.Count -eq 1) "expected exactly one signing failure, got: $($gate.Failures -join '; ')"
        Assert-True (($gate.Failures[0] -match 'Authenticode') -or ($gate.Failures[0] -match 'signtool')) 'the single failure must be the Authenticode re-verification'
    }

    New-TestCase 'evidence-direct-validation-binds-sha-and-hash' {
        $evidencePath = Join-Path $testRoot 'h-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha ('b' * 64))
        $result = Test-ExternalEvidenceFile $evidencePath $goodSha ('b' * 64)
        Assert-True ($result.Valid) "valid evidence must pass: $($result.Failures -join '; ')"
        $wrong = Test-ExternalEvidenceFile $evidencePath $goodSha ('c' * 64)
        Assert-True (-not $wrong.Valid) 'the same evidence must fail against a different artifact hash'
    }

    Write-Host ''
    Write-Host '==> release-qualify.ps1 early gates (scratch git repository)' -ForegroundColor Cyan

    $scratch = Join-Path $testRoot 'scratch-repo'
    New-Item -ItemType Directory -Path $scratch -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $scratch 'scripts') -Force | Out-Null
    # release-qualify.ps1 derives the repository root from its own location,
    # so the scripts live in a `scripts` subdirectory of the scratch repo.
    Copy-Item -LiteralPath $qualifyScript -Destination (Join-Path $scratch 'scripts\release-qualify.ps1')
    Copy-Item -LiteralPath $modulePath -Destination (Join-Path $scratch 'scripts\release-tooling.ps1')
    Copy-Item -LiteralPath $signScript -Destination (Join-Path $scratch 'scripts\sign-release.ps1')
    Push-Location $scratch
    try {
        git init -q 2>&1 | Out-Null
        git -c user.email=test@tabdock.invalid -c user.name='Test' add -A 2>&1 | Out-Null
        git -c user.email=test@tabdock.invalid -c user.name='Test' commit -q -m 'scratch' 2>&1 | Out-Null
        $scratchSha = (git rev-parse HEAD).Trim()

        New-TestCase 'dirty-local-release-candidate-refused' {
            $dirtyFile = Join-Path $scratch 'dirty-marker.txt'
            [IO.File]::WriteAllText($dirtyFile, 'dirty', [Text.UTF8Encoding]::new($false))
            try {
                $output = & (Join-Path $scratch 'scripts\release-qualify.ps1') -Sha $scratchSha -SkipOpenSpec 6>&1 2>&1 | Out-String
                Assert-True ($LASTEXITCODE -ne 0) "dirty-tree qualification must fail`n$output"
                Assert-True ($output -match 'Dirty working tree') "failure must cite the dirty working tree`n$output"
            }
            finally {
                Remove-Item -LiteralPath $dirtyFile -Force -ErrorAction SilentlyContinue
            }
        }

        New-TestCase 'wrong-candidate-sha-refused' {
            $bogusSha = 'f' * 40
            $output = & (Join-Path $scratch 'scripts\release-qualify.ps1') -Sha $bogusSha -SkipOpenSpec 6>&1 2>&1 | Out-String
            Assert-True ($LASTEXITCODE -ne 0) "wrong-SHA qualification must fail`n$output"
            Assert-True ($output -match 'Exact-SHA mismatch') "failure must cite the exact-SHA mismatch`n$output"
        }
    }
    finally {
        Pop-Location
    }

    Write-Host ''
    if ($failureList.Count -gt 0) {
        Write-Host "release-tooling tests: $passCount passed, $($failureList.Count) FAILED" -ForegroundColor Red
        exit 1
    }
    Write-Host "release-tooling tests: $passCount passed, 0 failed" -ForegroundColor Green
    exit 0
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
