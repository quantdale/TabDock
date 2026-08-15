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

    Two-stage release architecture coverage (production candidate -> publish
    the SAME bytes) additionally proves: the version authority chain
    (csproj <Version> authoritative; workflow expected version, manifest
    version, recorded binary identity, and informational version must all
    agree; malformed versions fail), the derived-tag contract (tag is always
    "v" + semanticVersion; arbitrary tags are impossible), the cross-run
    candidate binding (evidence and manifest must name the exact Stage A run
    and artifact; qualification-only artifacts are rejected), the Windows
    10/11 compatibility gate (missing/FAIL/BLOCKED/malformed entries fail
    closed), completedAt quality (ISO-8601, not materially in the future),
    and static workflow guarantees (the publish workflow contains no build,
    sign, or qualification invocation; the candidate-preparation workflow
    forces signing and never creates a release; the RC workflow has no
    publication path).

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
        [bool]$Mock = $false,
        [string]$ReleaseMode = 'PRODUCTION',
        [string]$WorkflowRunId = '123456789',
        [string]$BuildIdentitySemantic = '',
        [string]$BuildIdentityInfo = ''
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
        releaseMode            = $ReleaseMode
        workflowRunId          = $WorkflowRunId
        buildIdentity          = [ordered]@{
            semanticVersion      = if ($BuildIdentitySemantic) { $BuildIdentitySemantic } else { $Version }
            informationalVersion = if ($BuildIdentityInfo) { $BuildIdentityInfo } else { $Version + '+abcdef1' }
            selfReportedSha256   = 'unavailable'
        }
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
        [string]$Version = '1.0.0',
        [string]$ReleaseMode = 'PRODUCTION',
        [string]$WorkflowRunId = '123456789'
    )
    $dir = Join-Path $Parent $Name
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    $exe = Join-Path $dir 'TabDock.exe'
    New-DummyArtifact $exe
    $manifest = New-TestManifest -SourceSha $SourceSha -Version $Version -UnsignedSha (Get-FileSha256Lower $exe) `
        -ReleaseMode $ReleaseMode -WorkflowRunId $WorkflowRunId
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

function Get-TestTimestamp {
    # Deterministic, always-valid completion timestamps: one hour in the past.
    return [DateTimeOffset]::UtcNow.AddHours(-1).ToString('O')
}

function Get-CandidateArtifactName {
    param(
        [string]$SourceSha = ('a' * 40),
        [string]$RunId = '123456789'
    )
    return "tabdock-candidate-$SourceSha-$RunId"
}

function New-TestEvidence {
    param(
        [string]$SourceSha = ('a' * 40),
        [string]$ArtifactSha = ('b' * 64),
        [string]$RunId = '123456789',
        [string]$ArtifactName = ''
    )
    if ([string]::IsNullOrWhiteSpace($ArtifactName)) {
        $ArtifactName = Get-CandidateArtifactName -SourceSha $SourceSha -RunId $RunId
    }
    $at = Get-TestTimestamp
    return [ordered]@{
        schemaVersion          = 2
        sourceCommitSha        = $SourceSha
        artifactSha256         = $ArtifactSha
        candidateWorkflowRunId = $RunId
        candidateArtifactName  = $ArtifactName
        finalWindowsHumanSmoke = [ordered]@{
            status      = 'PASS'
            completedAt = $at
            operator    = 'Test Operator'
            evidence    = 'manual smoke executed against the exact artifact'
        }
        physicalMixedDpi = [ordered]@{
            status      = 'PASS'
            completedAt = $at
            operator    = 'Test Operator'
            evidence    = 'mixed-DPI qualification executed against the exact artifact'
        }
        windowsCompatibility = [ordered]@{
            status = 'PASS'
            windows10 = [ordered]@{
                status             = 'PASS'
                build              = '10.0.19045.x'
                operator           = 'Test Operator'
                completedAt        = $at
                nativeAbiEvidence  = '--selftest-native-abi PASS on Windows 10 x64 build 10.0.19045.x (environment report attached)'
                evidence           = 'real Windows 10 x64 qualification executed against the exact artifact'
            }
            windows11 = [ordered]@{
                status             = 'PASS'
                build              = '10.0.26200'
                operator           = 'Test Operator'
                completedAt        = $at
                nativeAbiEvidence  = '--selftest-native-abi PASS on Windows 11 x64 build 10.0.26200 (environment report attached)'
                evidence           = 'real Windows 11 x64 qualification executed against the exact artifact'
            }
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
    $goodRunId = '123456789'
    $goodArtifactName = Get-CandidateArtifactName -SourceSha $goodSha -RunId $goodRunId

    New-TestCase 'unsigned-artifact-rejected-when-production-signing-mandatory' {
        $art = New-SyntheticArtifactDir $testRoot 'g-unsigned-mandatory' -SourceSha $goodSha -Version $goodVersion
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -RequireSigning -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName
        Assert-True (-not $gate.Eligible) 'unsigned artifact must be ineligible under mandatory production signing'
        Assert-True (($gate.Failures -join ';') -match 'signing') 'failure must identify the signing policy'
    }

    New-TestCase 'manifest-signing-failure-state-rejected' {
        $art = New-SyntheticArtifactDir $testRoot 'g-signing-failed' -SourceSha $goodSha -Version $goodVersion
        Update-TestManifest $art.ManifestPath @{ signingStatus = 'SIGNING_FAILED'; signatureVerification = 'FAILED' }
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -RequireSigning -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName
        Assert-True (-not $gate.Eligible) 'a SIGNING_FAILED manifest must never be production-eligible'
    }

    New-TestCase 'signature-verification-failure-rejected' {
        $art = New-SyntheticArtifactDir $testRoot 'g-verify-failed' -SourceSha $goodSha -Version $goodVersion
        Update-TestManifest $art.ManifestPath @{ signingStatus = 'SIGNED'; signatureVerification = 'FAILED'; finalSignedSha256 = $art.ArtifactSha256 }
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -RequireSigning -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName
        Assert-True (-not $gate.Eligible) 'an unverified signature must never be treated as valid for production'
        Assert-True (($gate.Failures -join ';') -match 'signatureVerification') 'failure must cite signature verification'
    }

    New-TestCase 'mock-signed-artifact-rejected-for-production' {
        $art = New-SyntheticArtifactDir $testRoot 'g-mock' -SourceSha $goodSha -Version $goodVersion
        Update-TestManifest $art.ManifestPath @{ signingStatus = 'SIGNED'; signatureVerification = 'SIGNATURE_VERIFIED'; finalSignedSha256 = $art.ArtifactSha256; signingMock = $true }
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -RequireSigning -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName
        Assert-True (-not $gate.Eligible) 'a test-only mock-signed artifact must never be production-eligible'
        Assert-True (($gate.Failures -join ';') -match 'mock') 'failure must identify mock signing'
    }

    New-TestCase 'missing-external-evidence-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'g-missing-evidence' -SourceSha $goodSha -Version $goodVersion
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath (Join-Path $art.Dir 'does-not-exist.json') -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName
        Assert-True (-not $gate.Eligible) 'missing external evidence must fail closed'
    }

    New-TestCase 'malformed-external-evidence-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'g-malformed-evidence' -SourceSha $goodSha -Version $goodVersion
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        [IO.File]::WriteAllText($evidencePath, '{ not json', [Text.UTF8Encoding]::new($false))
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName
        Assert-True (-not $gate.Eligible) 'malformed external evidence must fail closed'
    }

    New-TestCase 'evidence-from-wrong-source-sha-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'g-wrong-sha' -SourceSha $goodSha -Version $goodVersion
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha ('d' * 40) -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName
        Assert-True (-not $gate.Eligible) 'evidence bound to another source SHA must fail closed'
        Assert-True (($gate.Failures -join ';') -match 'sourceCommitSha') 'failure must cite the SHA binding'
    }

    New-TestCase 'evidence-from-wrong-artifact-hash-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'g-wrong-hash' -SourceSha $goodSha -Version $goodVersion
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha ('e' * 64))
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName
        Assert-True (-not $gate.Eligible) 'evidence bound to another artifact must fail closed'
        Assert-True (($gate.Failures -join ';') -match 'artifactSha256') 'failure must cite the artifact binding'
    }

    New-TestCase 'smoke-fail-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'g-smoke-fail' -SourceSha $goodSha -Version $goodVersion
        $evidence = New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256
        $evidence.finalWindowsHumanSmoke.status = 'FAIL'
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath $evidence
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName
        Assert-True (-not $gate.Eligible) 'finalWindowsHumanSmoke=FAIL must fail closed'
    }

    New-TestCase 'smoke-blocked-external-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'g-smoke-blocked' -SourceSha $goodSha -Version $goodVersion
        $evidence = New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256
        $evidence.finalWindowsHumanSmoke.status = 'BLOCKED_EXTERNAL'
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath $evidence
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName
        Assert-True (-not $gate.Eligible) 'finalWindowsHumanSmoke=BLOCKED_EXTERNAL must fail closed'
    }

    New-TestCase 'dpi-fail-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'g-dpi-fail' -SourceSha $goodSha -Version $goodVersion
        $evidence = New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256
        $evidence.physicalMixedDpi.status = 'FAIL'
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath $evidence
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName
        Assert-True (-not $gate.Eligible) 'physicalMixedDpi=FAIL must fail closed'
    }

    New-TestCase 'dpi-blocked-external-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'g-dpi-blocked' -SourceSha $goodSha -Version $goodVersion
        $evidence = New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256
        $evidence.physicalMixedDpi.status = 'BLOCKED_NO_MIXED_DPI_HARDWARE'
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath $evidence
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName
        Assert-True (-not $gate.Eligible) 'physicalMixedDpi=BLOCKED_NO_MIXED_DPI_HARDWARE must fail closed for production'
    }

    New-TestCase 'evidence-missing-gate-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'g-missing-gate' -SourceSha $goodSha -Version $goodVersion
        $evidence = New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256
        $evidence.Remove('physicalMixedDpi')
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath $evidence
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName
        Assert-True (-not $gate.Eligible) 'evidence without a mandatory gate must fail closed'
    }

    New-TestCase 'evidence-missing-operator-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'g-no-operator' -SourceSha $goodSha -Version $goodVersion
        $evidence = New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256
        $evidence.finalWindowsHumanSmoke.operator = ''
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath $evidence
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName
        Assert-True (-not $gate.Eligible) 'evidence without an operator must fail closed'
    }

    New-TestCase 'evidence-wrong-schema-version-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'g-schema' -SourceSha $goodSha -Version $goodVersion
        $evidence = New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256
        $evidence.schemaVersion = 99
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath $evidence
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName
        Assert-True (-not $gate.Eligible) 'wrong evidence schema version must fail closed'
    }

    New-TestCase 'manifest-hash-mismatch-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'g-manifest-hash' -SourceSha $goodSha -Version $goodVersion
        Update-TestManifest $art.ManifestPath @{ artifactSha256 = ('f' * 64) }
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName
        Assert-True (-not $gate.Eligible) 'manifest hash disagreement must fail closed'
    }

    New-TestCase 'sha256sums-mismatch-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'g-sums' -SourceSha $goodSha -Version $goodVersion
        [IO.File]::WriteAllText($art.SumsPath, ('0' * 64) + "  TabDock.exe`r`n", [Text.UTF8Encoding]::new($false))
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName
        Assert-True (-not $gate.Eligible) 'SHA256SUMS disagreement must fail closed'
    }

    New-TestCase 'final-executable-tampered-after-qualification-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'g-tampered' -SourceSha $goodSha -Version $goodVersion
        $bytes = [IO.File]::ReadAllBytes($art.Exe)
        $bytes[0] = $bytes[0] -bxor 0xFF
        [IO.File]::WriteAllBytes($art.Exe, $bytes)
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName
        Assert-True (-not $gate.Eligible) 'a tampered final executable must fail closed'
    }

    New-TestCase 'wrong-semantic-version-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'g-version' -SourceSha $goodSha -Version $goodVersion
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion '9.9.9' -EvidencePath $evidencePath -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName
        Assert-True (-not $gate.Eligible) 'a version disagreement must fail closed'
    }

    New-TestCase 'wrong-candidate-sha-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'g-sha' -SourceSha $goodSha -Version $goodVersion
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha ('c' * 40) -ExpectedVersion $goodVersion -EvidencePath $evidencePath -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName
        Assert-True (-not $gate.Eligible) 'a requested-SHA disagreement must fail closed'
    }

    New-TestCase 'failed-qualification-manifest-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'g-qual-failed' -SourceSha $goodSha -Version $goodVersion
        Update-TestManifest $art.ManifestPath @{ qualificationStatus = 'FAIL' }
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName
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
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName
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
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -RequireSigning -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName
        Assert-True (-not $gate.Eligible) 'unsigned synthetic artifact must fail mandatory signing'
        Assert-True ($gate.Failures.Count -eq 1) "expected exactly one signing failure, got: $($gate.Failures -join '; ')"
        Assert-True (($gate.Failures[0] -match 'Authenticode') -or ($gate.Failures[0] -match 'signtool')) 'the single failure must be the Authenticode re-verification'
    }

    New-TestCase 'evidence-direct-validation-binds-sha-and-hash' {
        $evidencePath = Join-Path $testRoot 'h-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha ('b' * 64))
        $result = Test-ExternalEvidenceFile $evidencePath $goodSha ('b' * 64) -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName
        Assert-True ($result.Valid) "valid evidence must pass: $($result.Failures -join '; ')"
        $wrong = Test-ExternalEvidenceFile $evidencePath $goodSha ('c' * 64) -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName
        Assert-True (-not $wrong.Valid) 'the same evidence must fail against a different artifact hash'
    }

    Write-Host ''
    Write-Host '==> Version authority: project -> binary -> manifest -> tag' -ForegroundColor Cyan

    New-TestCase 'project-version-is-authoritative-for-the-real-project' {
        $version = Get-ProjectSemanticVersion (Join-Path $repoRoot 'TabDock.csproj')
        Assert-True ($version -eq '1.0.0') "real TabDock.csproj <Version> must be 1.0.0, got '$version'"
    }

    New-TestCase 'malformed-semantic-version-rejected' {
        foreach ($bad in @('1.0', '1.0.0.1', 'banana', 'v1.0.0', '1..0', '1.0.0-', '01.0.0', '1.0.0+build')) {
            Assert-True (-not (Test-SemanticVersion $bad)) "Test-SemanticVersion must reject '$bad'"
        }
        foreach ($good in @('1.0.0', '0.1.2', '10.20.30', '1.0.0-rc.1', '1.0.0-alpha.2')) {
            Assert-True (Test-SemanticVersion $good) "Test-SemanticVersion must accept '$good'"
        }
    }

    New-TestCase 'project-version-property-expression-refused' {
        $fake = Join-Path $testRoot 'expr.csproj'
        [IO.File]::WriteAllText($fake, '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><Version>$(VersionFromAnotherProperty)</Version></PropertyGroup></Project>', [Text.UTF8Encoding]::new($false))
        Assert-Throws 'a property-expression <Version> must be refused' { $null = Get-ProjectSemanticVersion $fake }
    }

    New-TestCase 'project-version-malformed-refused' {
        $fake = Join-Path $testRoot 'bad.csproj'
        [IO.File]::WriteAllText($fake, '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><Version>banana</Version></PropertyGroup></Project>', [Text.UTF8Encoding]::new($false))
        Assert-Throws 'a malformed <Version> must be refused' { $null = Get-ProjectSemanticVersion $fake }
    }

    New-TestCase 'tag-is-derived-v-plus-semantic-version' {
        Assert-True ((Get-ReleaseTagFromVersion '1.0.0') -eq 'v1.0.0') 'version 1.0.0 must derive the tag v1.0.0'
        Assert-True ((Assert-ReleaseTagMatchesVersion 'v1.0.0' '1.0.0') -eq 'v1.0.0') 'version 1.0.0 + tag v1.0.0 must be allowed'
        Assert-Throws 'version 1.0.0 + tag v2.0.0 must fail' { $null = Assert-ReleaseTagMatchesVersion 'v2.0.0' '1.0.0' }
        Assert-Throws 'version 1.0.0 + tag stable must fail' { $null = Assert-ReleaseTagMatchesVersion 'stable' '1.0.0' }
        Assert-Throws 'version 1.0.0 + tag v1.0.1 must fail' { $null = Assert-ReleaseTagMatchesVersion 'v1.0.1' '1.0.0' }
        Assert-Throws 'a malformed version must never derive a tag' { $null = Get-ReleaseTagFromVersion '1.0' }
    }

    New-TestCase 'malformed-manifest-semantic-version-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'v-malformed' -SourceSha $goodSha -Version $goodVersion
        Update-TestManifest $art.ManifestPath @{ semanticVersion = '9.9' }
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion '9.9' -EvidencePath $evidencePath -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName
        Assert-True (-not $gate.Eligible) 'a malformed manifest semantic version must fail closed'
        Assert-True (($gate.Failures -join ';') -match 'not a valid semantic version') 'failure must cite the malformed semantic version'
    }

    New-TestCase 'forged-binary-version-in-manifest-fails-closed' {
        # The manifest version is authoritative, but the binary identity
        # recorded in the manifest must AGREE with it; a forged binary version
        # (manifest says 1.0.0, binary identity says 9.9.9) fails closed.
        $art = New-SyntheticArtifactDir $testRoot 'v-binary-forge' -SourceSha $goodSha -Version $goodVersion
        Update-TestManifest $art.ManifestPath @{ buildIdentity = [ordered]@{ semanticVersion = '9.9.9'; informationalVersion = '9.9.9+abcdef1'; selfReportedSha256 = 'unavailable' } }
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName
        Assert-True (-not $gate.Eligible) 'a forged binary semantic version must fail closed'
        Assert-True (($gate.Failures -join ';') -match 'buildIdentity.semanticVersion') 'failure must cite the binary version binding'
    }

    New-TestCase 'forged-informational-version-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'v-info-forge' -SourceSha $goodSha -Version $goodVersion
        Update-TestManifest $art.ManifestPath @{ buildIdentity = [ordered]@{ semanticVersion = '1.0.0'; informationalVersion = '2.0.0+abcdef1'; selfReportedSha256 = 'unavailable' } }
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName
        Assert-True (-not $gate.Eligible) 'an informational version without the manifest semantic version must fail closed'
        Assert-True (($gate.Failures -join ';') -match 'informationalVersion') 'failure must cite the informational version binding'
    }

    New-TestCase 'rc-qualification-only-artifact-rejected-for-production' {
        $art = New-SyntheticArtifactDir $testRoot 'v-rc-mode' -SourceSha $goodSha -Version $goodVersion -ReleaseMode 'QUALIFICATION_ONLY'
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName
        Assert-True (-not $gate.Eligible) 'a qualification-only artifact must never be production-publishable'
        Assert-True (($gate.Failures -join ';') -match 'releaseMode') 'failure must cite the release mode'
    }

    Write-Host ''
    Write-Host '==> Cross-run candidate binding (Stage A -> Stage B provenance)' -ForegroundColor Cyan

    New-TestCase 'candidate-run-binding-required' {
        # Evidence naming another Stage A run must fail even when everything
        # else (SHA, hash, gates) matches.
        $art = New-SyntheticArtifactDir $testRoot 'r-wrong-run-evidence' -SourceSha $goodSha -Version $goodVersion
        $evidence = New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256 -RunId '999999999'
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath $evidence
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName
        Assert-True (-not $gate.Eligible) 'evidence naming another Stage A run must fail closed'
        Assert-True (($gate.Failures -join ';') -match 'candidateWorkflowRunId') 'failure must cite the run binding'
    }

    New-TestCase 'manifest-workflow-run-mismatch-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'r-manifest-run' -SourceSha $goodSha -Version $goodVersion -WorkflowRunId '555555555'
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName
        Assert-True (-not $gate.Eligible) 'a manifest produced by another run must fail closed'
        Assert-True (($gate.Failures -join ';') -match 'workflowRunId') 'failure must cite the manifest run binding'
    }

    New-TestCase 'candidate-artifact-name-binding-required' {
        $art = New-SyntheticArtifactDir $testRoot 'r-wrong-name' -SourceSha $goodSha -Version $goodVersion
        $evidence = New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256
        $evidence.candidateArtifactName = 'tabdock-candidate-' + ('c' * 40) + '-123456789'
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath $evidence
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName
        Assert-True (-not $gate.Eligible) 'evidence naming another artifact must fail closed'
        Assert-True (($gate.Failures -join ';') -match 'candidateArtifactName') 'failure must cite the artifact-name binding'
    }

    New-TestCase 'malformed-candidate-run-id-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'r-malformed-run' -SourceSha $goodSha -Version $goodVersion
        $evidence = New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256
        $evidence.candidateWorkflowRunId = 'not-a-number'
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath $evidence
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName
        Assert-True (-not $gate.Eligible) 'a malformed candidate run id must fail closed'
    }

    New-TestCase 'missing-artifact-directory-fails-closed' {
        $gate = Test-PublicationEligibility -ArtifactDir (Join-Path $testRoot 'does-not-exist') -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath (Join-Path $testRoot 'nope.json') -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName
        Assert-True (-not $gate.Eligible) 'a missing/expired artifact directory must fail closed'
        Assert-True (($gate.Failures -join ';') -match 'final artifact missing') 'failure must cite the missing artifact'
    }

    New-TestCase 'existing-valid-signed-candidate-accepted-by-non-network-gate' {
        # The Stage B gate accepts a fully consistent production candidate
        # (PRODUCTION mode, run-bound manifest, schema-v2 evidence) without
        # any network; the only non-signing gate condition is the independent
        # Authenticode check, which is exercised with RequireSigning.
        $art = New-SyntheticArtifactDir $testRoot 'r-happy' -SourceSha $goodSha -Version $goodVersion
        Update-TestManifest $art.ManifestPath @{ signingStatus = 'SIGNED'; signatureVerification = 'SIGNATURE_VERIFIED'; finalSignedSha256 = $art.ArtifactSha256 }
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName
        Assert-True ($gate.Eligible) "a fully bound production candidate must pass the non-network gate: $($gate.Failures -join '; ')"
    }

    Write-Host ''
    Write-Host '==> Windows compatibility gate (Windows 10 + Windows 11 evidence)' -ForegroundColor Cyan

    New-TestCase 'windows-compatibility-missing-fails-closed' {
        $evidence = New-TestEvidence -SourceSha $goodSha -ArtifactSha ('b' * 64)
        $evidence.Remove('windowsCompatibility')
        $evidencePath = Join-Path $testRoot 'w-missing.json'
        Save-TestEvidence $evidencePath $evidence
        $result = Test-ExternalEvidenceFile $evidencePath $goodSha ('b' * 64) -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName
        Assert-True (-not $result.Valid) 'evidence without windowsCompatibility must fail closed'
        Assert-True (($result.Failures -join ';') -match 'windowsCompatibility') 'failure must cite the missing gate'
    }

    New-TestCase 'windows-compatibility-status-fail-fails-closed' {
        $evidence = New-TestEvidence -SourceSha $goodSha -ArtifactSha ('b' * 64)
        $evidence.windowsCompatibility.status = 'FAIL'
        $evidencePath = Join-Path $testRoot 'w-fail.json'
        Save-TestEvidence $evidencePath $evidence
        $result = Test-ExternalEvidenceFile $evidencePath $goodSha ('b' * 64) -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName
        Assert-True (-not $result.Valid) 'windowsCompatibility status FAIL must fail closed'
    }

    New-TestCase 'windows10-missing-fails-closed' {
        $evidence = New-TestEvidence -SourceSha $goodSha -ArtifactSha ('b' * 64)
        $evidence.windowsCompatibility.Remove('windows10')
        $evidencePath = Join-Path $testRoot 'w-no10.json'
        Save-TestEvidence $evidencePath $evidence
        $result = Test-ExternalEvidenceFile $evidencePath $goodSha ('b' * 64) -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName
        Assert-True (-not $result.Valid) 'evidence without Windows 10 entry must fail closed'
        Assert-True (($result.Failures -join ';') -match 'windows10') 'failure must cite the Windows 10 entry'
    }

    New-TestCase 'windows10-status-blocked-fails-closed' {
        $evidence = New-TestEvidence -SourceSha $goodSha -ArtifactSha ('b' * 64)
        $evidence.windowsCompatibility.windows10.status = 'BLOCKED_EXTERNAL'
        $evidencePath = Join-Path $testRoot 'w-10blocked.json'
        Save-TestEvidence $evidencePath $evidence
        $result = Test-ExternalEvidenceFile $evidencePath $goodSha ('b' * 64) -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName
        Assert-True (-not $result.Valid) 'Windows 10 BLOCKED_EXTERNAL must fail closed for production'
    }

    New-TestCase 'windows10-missing-build-fails-closed' {
        $evidence = New-TestEvidence -SourceSha $goodSha -ArtifactSha ('b' * 64)
        $evidence.windowsCompatibility.windows10.build = ''
        $evidencePath = Join-Path $testRoot 'w-10build.json'
        Save-TestEvidence $evidencePath $evidence
        $result = Test-ExternalEvidenceFile $evidencePath $goodSha ('b' * 64) -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName
        Assert-True (-not $result.Valid) 'Windows 10 evidence without a recorded build must fail closed'
        Assert-True (($result.Failures -join ';') -match 'build') 'failure must cite the missing OS build'
    }

    New-TestCase 'windows10-missing-native-abi-evidence-fails-closed' {
        $evidence = New-TestEvidence -SourceSha $goodSha -ArtifactSha ('b' * 64)
        $evidence.windowsCompatibility.windows10.nativeAbiEvidence = ''
        $evidencePath = Join-Path $testRoot 'w-10abi.json'
        Save-TestEvidence $evidencePath $evidence
        $result = Test-ExternalEvidenceFile $evidencePath $goodSha ('b' * 64) -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName
        Assert-True (-not $result.Valid) 'Windows 10 evidence without the native ABI selftest report must fail closed'
        Assert-True (($result.Failures -join ';') -match 'nativeAbiEvidence') 'failure must cite the native ABI evidence'
    }

    New-TestCase 'windows11-status-fail-fails-closed' {
        $evidence = New-TestEvidence -SourceSha $goodSha -ArtifactSha ('b' * 64)
        $evidence.windowsCompatibility.windows11.status = 'FAIL'
        $evidencePath = Join-Path $testRoot 'w-11fail.json'
        Save-TestEvidence $evidencePath $evidence
        $result = Test-ExternalEvidenceFile $evidencePath $goodSha ('b' * 64) -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName
        Assert-True (-not $result.Valid) 'Windows 11 status FAIL must fail closed'
    }

    New-TestCase 'windows-compatibility-happy-path-passes' {
        $evidencePath = Join-Path $testRoot 'w-happy.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha ('b' * 64))
        $result = Test-ExternalEvidenceFile $evidencePath $goodSha ('b' * 64) -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName
        Assert-True ($result.Valid) "valid Windows 10/11 evidence must pass: $($result.Failures -join '; ')"
    }

    Write-Host ''
    Write-Host '==> completedAt quality (ISO-8601, not in the future)' -ForegroundColor Cyan

    New-TestCase 'completed-at-not-iso-fails-closed' {
        $evidence = New-TestEvidence -SourceSha $goodSha -ArtifactSha ('b' * 64)
        $evidence.finalWindowsHumanSmoke.completedAt = 'tomorrow-ish'
        $evidencePath = Join-Path $testRoot 'c-not-iso.json'
        Save-TestEvidence $evidencePath $evidence
        $result = Test-ExternalEvidenceFile $evidencePath $goodSha ('b' * 64) -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName
        Assert-True (-not $result.Valid) 'a non-ISO completedAt must fail closed'
        Assert-True (($result.Failures -join ';') -match 'ISO-8601') 'failure must cite the timestamp format'
    }

    New-TestCase 'completed-at-in-future-fails-closed' {
        $evidence = New-TestEvidence -SourceSha $goodSha -ArtifactSha ('b' * 64)
        $evidence.physicalMixedDpi.completedAt = [DateTimeOffset]::UtcNow.AddHours(1).ToString('O')
        $evidencePath = Join-Path $testRoot 'c-future.json'
        Save-TestEvidence $evidencePath $evidence
        $result = Test-ExternalEvidenceFile $evidencePath $goodSha ('b' * 64) -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName
        Assert-True (-not $result.Valid) 'a future completedAt must fail closed'
        Assert-True (($result.Failures -join ';') -match 'future') 'failure must cite the future timestamp'
    }

    New-TestCase 'windows-compatibility-completed-at-validated' {
        $evidence = New-TestEvidence -SourceSha $goodSha -ArtifactSha ('b' * 64)
        $evidence.windowsCompatibility.windows10.completedAt = 'not-a-date'
        $evidencePath = Join-Path $testRoot 'c-wc.json'
        Save-TestEvidence $evidencePath $evidence
        $result = Test-ExternalEvidenceFile $evidencePath $goodSha ('b' * 64) -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName
        Assert-True (-not $result.Valid) 'a malformed Windows 10 completedAt must fail closed'
    }

    Write-Host ''
    Write-Host '==> Two-stage workflow structural guarantees (static workflow review)' -ForegroundColor Cyan

    New-TestCase 'publish-workflow-cannot-build-sign-or-qualify' {
        $yml = [IO.File]::ReadAllText((Join-Path $repoRoot '.github\workflows\publish-release.yml'))
        foreach ($forbidden in @('dotnet publish', 'sign-release.ps1', 'release-qualify.ps1', 'upload-artifact', 'RELEASE_SIGNING_REQUIRED', 'create-release')) {
            Assert-True ($yml -notmatch [regex]::Escape($forbidden)) "publish-release.yml must not contain '$forbidden' (Stage B never builds, signs, or qualifies)"
        }
        Assert-True ($yml -match 'actions/download-artifact@v7') 'Stage B must download the Stage A artifact'
        Assert-True ($yml -match 'run-id') 'Stage B must bind to the Stage A run id'
        Assert-True ($yml -match 'actions: read') 'Stage B needs actions: read for the cross-run artifact download'
        Assert-True ($yml -match 'gh release create') 'Stage B must be the only workflow that creates the release'
        Assert-True ($yml -match 'Get-ReleaseTagFromVersion') 'Stage B must derive the tag from the semantic version'
    }

    New-TestCase 'prepare-candidate-workflow-forces-signing-and-never-publishes' {
        $yml = [IO.File]::ReadAllText((Join-Path $repoRoot '.github\workflows\prepare-release-candidate.yml'))
        Assert-True ($yml -match "RELEASE_SIGNING_REQUIRED:\s*'true'") 'Stage A must force signing'
        Assert-True ($yml -match "RELEASE_PRODUCTION_GATE:\s*'true'") 'Stage A must force the production gate'
        Assert-True ($yml -match 'BLOCKED_EXTERNAL') 'Stage A must block explicitly when signing credentials are missing'
        Assert-True ($yml -notmatch 'gh release create') 'Stage A must never create a GitHub Release'
        Assert-True ($yml -notmatch 'create-release') 'Stage A has no release-creation input'
        Assert-True ($yml -match 'actions/upload-artifact@v7') 'Stage A must retain the candidate artifact'
        Assert-True ($yml -match 'tabdock-candidate-') 'Stage A artifact names follow the candidate scheme'
    }

    New-TestCase 'rc-workflow-has-no-publication-path' {
        $yml = [IO.File]::ReadAllText((Join-Path $repoRoot '.github\workflows\release.yml'))
        Assert-True ($yml -notmatch 'gh release create') 'the RC workflow must never create a release'
        Assert-True ($yml -notmatch 'create-release') 'the RC workflow has no release-creation input'
        Assert-True ($yml -notmatch 'external-evidence') 'the RC workflow never consumes external evidence'
        Assert-True ($yml -notmatch 'download-artifact') 'the RC workflow never downloads artifacts'
        Assert-True ($yml -match 'tabdock-rc-') 'RC artifacts use the rc naming scheme'
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
    # Minimal project file so the version-authority gates are testable
    # (release-qualify.ps1 reads <Version> from the repository root csproj).
    [IO.File]::WriteAllText((Join-Path $scratch 'TabDock.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><Version>1.0.0</Version></PropertyGroup></Project>', [Text.UTF8Encoding]::new($false))
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

        New-TestCase 'workflow-expected-version-mismatch-fails' {
            # version=9.9.9 supplied while the project declares 1.0.0: the
            # workflow input is only an EXPECTED value and must fail.
            $output = & (Join-Path $scratch 'scripts\release-qualify.ps1') -Sha $scratchSha -Version 9.9.9 -SkipOpenSpec 6>&1 2>&1 | Out-String
            Assert-True ($LASTEXITCODE -ne 0) "an expected-version disagreement must fail`n$output"
            Assert-True ($output -match 'Version authority mismatch') "failure must cite the authority mismatch`n$output"
        }

        New-TestCase 'malformed-project-version-fails' {
            [IO.File]::WriteAllText((Join-Path $scratch 'TabDock.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><Version>banana</Version></PropertyGroup></Project>', [Text.UTF8Encoding]::new($false))
            git -c user.email=test@tabdock.invalid -c user.name='Test' add -A 2>&1 | Out-Null
            git -c user.email=test@tabdock.invalid -c user.name='Test' commit -q -m 'scratch malformed version' 2>&1 | Out-Null
            $newSha = (git rev-parse HEAD).Trim()
            $output = & (Join-Path $scratch 'scripts\release-qualify.ps1') -Sha $newSha -Version 1.0.0 -SkipOpenSpec 6>&1 2>&1 | Out-String
            Assert-True ($LASTEXITCODE -ne 0) "a malformed project version must fail`n$output"
            Assert-True ($output -match 'not a valid semantic version') "failure must cite the malformed project version`n$output"
        }

        New-TestCase 'expected-version-vs-different-project-version-fails' {
            # The project now declares 2.0.0; the workflow still expects
            # 1.0.0. The project version is authoritative.
            [IO.File]::WriteAllText((Join-Path $scratch 'TabDock.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><Version>2.0.0</Version></PropertyGroup></Project>', [Text.UTF8Encoding]::new($false))
            git -c user.email=test@tabdock.invalid -c user.name='Test' add -A 2>&1 | Out-Null
            git -c user.email=test@tabdock.invalid -c user.name='Test' commit -q -m 'scratch version 2.0.0' 2>&1 | Out-Null
            $newSha = (git rev-parse HEAD).Trim()
            $output = & (Join-Path $scratch 'scripts\release-qualify.ps1') -Sha $newSha -Version 1.0.0 -SkipOpenSpec 6>&1 2>&1 | Out-String
            Assert-True ($LASTEXITCODE -ne 0) "an expected version that disagrees with the project version must fail`n$output"
            Assert-True ($output -match 'Version authority mismatch') "failure must cite the authority mismatch`n$output"
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
