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
    sign, or qualification invocation and no signing-provider authentication;
    the candidate-preparation workflow forces an APPROVED production signer
    and never creates a release; the RC workflow has no publication path).

    Signing-provider policy coverage proves the production chain no longer
    depends on an exportable PFX: SIGNING_PROVIDER selects the backend
    (not-configured / local-pfx / digicert-stm / mock-test), production
    policy approves ONLY the non-exportable HSM/cloud provider class
    (digicert-stm / CLOUD_HSM), production rejects local-pfx, mock,
    not-configured, unknown, and missing provider/key-protection metadata,
    the digicert-stm configuration preflight names missing credentials and
    fails closed (BLOCKED_EXTERNAL) without them, the signer contract always
    carries Provider/KeyProtection/TimestampStatus/certificate identity,
    mock results can never claim an approved provider, a provider-reported
    success is never trusted without independent Authenticode + RFC3161
    timestamp verification of the actual bytes, and the certificate identity
    recorded in the manifest is cross-checked against the file.

    The mock signer (scripts/sign-release.ps1 -MockSign* with
    SIGNING_PROVIDER=mock-test) models the real fact that Authenticode
    signing changes the artifact bytes. It performs NO Authenticode
    operation, is refused when real material is present and when the mock
    provider is not explicitly selected, is recorded as Mock=true with
    Provider=mock-test, and can never pass the production gate.

    Release-policy trust-boundary coverage (P0/P1) proves the candidate can
    never supply the policy that decides whether it may be published: the
    old pre-HSM candidate shape (SIGNED + verified + hash + no mock + valid
    evidence but no current provider/schema metadata) is rejected by the
    CURRENT policy; a manifest without releasePolicySchemaVersion or with a
    stale schema fails; hostile candidate release-tooling files can never
    change the Stage B verdict; Stage B loads the policy module exclusively
    from policy/ (never candidate-source/ or candidate-artifact/); the
    current trusted publisher policy (SIGNING_EXPECTED_SUBJECT) is
    mandatory, and a manifest+file that consistently record the wrong
    publisher still fail against the current policy; production Stage A
    receives no SIGNCERT_* PFX secrets; the DigiCert action is pinned to its
    full immutable SHA; the hosted build workflow gates the regression
    suite; the Stage B verify job has no contents: write and the publish job
    performs no build/sign/candidate execution; timestamp policy failures
    (missing/warned) fail closed.

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
        [string]$BuildIdentityInfo = '',
        [string]$Provider = 'not-configured',
        [string]$KeyProtection = 'NOT_CONFIGURED',
        [string]$TimestampStatus = 'NOT_PERFORMED',
        [string]$CertificateSubject = '',
        [string]$CertificateThumbprint = '',
        [string]$CertificateIssuer = '',
        [string]$CertificateValidFrom = '',
        [string]$CertificateValidTo = '',
        [string[]]$CertificateEku = @()
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
        signingProvider        = $Provider
        signingKeyProtection   = $KeyProtection
        timestampStatus        = $TimestampStatus
        signingCertificateSubject = if ($CertificateSubject) { $CertificateSubject } else { $null }
        signingCertificateThumbprint = if ($CertificateThumbprint) { $CertificateThumbprint } else { $null }
        signingCertificateIssuer = if ($CertificateIssuer) { $CertificateIssuer } else { $null }
        signingCertificateSerialNumber = $null
        signingCertificateValidFrom = if ($CertificateValidFrom) { $CertificateValidFrom } else { $null }
        signingCertificateValidTo = if ($CertificateValidTo) { $CertificateValidTo } else { $null }
        signingCertificateEku = if ($CertificateEku.Count -gt 0) { @($CertificateEku) } else { $null }
        timestampCertificateSubject = $null
        timestampCertificateThumbprint = $null
        signingMock            = if ($Mock) { $true } else { $null }
        qualificationStatus    = $QualificationStatus
        productionReleaseEligibility = 'BLOCKED_EXTERNAL'
        releaseMode            = $ReleaseMode
        releasePolicySchemaVersion = 3
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

function Set-SyntheticSignedManifest {
    <#
    .SYNOPSIS
        Marks a synthetic artifact manifest as SIGNED by the APPROVED
        production provider (digicert-stm / CLOUD_HSM) with a complete,
        internally consistent signing-provenance record (status,
        verification, final hash, certificate identity, verified timestamp).
        The dummy artifact itself is NOT Authenticode-signed, so the
        independent on-disk checks still fail closed where they run.
    #>
    param(
        [Parameter(Mandatory = $true)][string]$ManifestPath,
        [Parameter(Mandatory = $true)][string]$FinalSha
    )
    Update-TestManifest $ManifestPath @{
        signingStatus          = 'SIGNED'
        signatureVerification  = 'SIGNATURE_VERIFIED'
        finalSignedSha256      = $FinalSha
        signingProvider        = 'digicert-stm'
        signingKeyProtection   = 'CLOUD_HSM'
        timestampStatus        = 'VERIFIED'
        signingCertificateSubject = 'CN=TabDock Test Publisher, O=TabDock Test, C=US'
        signingCertificateThumbprint = '1111111111111111111111111111111111111111'
        signingCertificateIssuer = 'CN=TabDock Test CA, O=TabDock Test, C=US'
        signingCertificateSerialNumber = '0102030405060708'
        signingCertificateValidFrom = '2026-01-01T00:00:00Z'
        signingCertificateValidTo = '2029-01-01T00:00:00Z'
        signingCertificateEku  = @('1.3.6.1.5.5.7.3.3')
        timestampCertificateSubject = 'CN=DigiCert Timestamp Responder, O=DigiCert Inc, C=US'
        timestampCertificateThumbprint = '2222222222222222222222222222222222222222'
    }
}

function Invoke-TestMockSigner {
    <#
    .SYNOPSIS
        Runs sign-release.ps1 in a test-only mock mode with
        SIGNING_PROVIDER=mock-test explicitly set (mock modes refuse to run
        without the explicit mock provider) and restores the previous
        environment afterwards.
    #>
    param(
        [Parameter(Mandatory = $true)][string]$ExePath,
        [ValidateSet('MockSign', 'MockSignFailure', 'MockVerifyFailure')][string]$Mode = 'MockSign'
    )
    $previous = [Environment]::GetEnvironmentVariable('SIGNING_PROVIDER')
    [Environment]::SetEnvironmentVariable('SIGNING_PROVIDER', 'mock-test')
    try {
        switch ($Mode) {
            'MockSign'          { return & $signScript -ExePath $ExePath -MockSign 2>&1 | Out-String }
            'MockSignFailure'   { return & $signScript -ExePath $ExePath -MockSignFailure 2>&1 | Out-String }
            'MockVerifyFailure' { return & $signScript -ExePath $ExePath -MockVerifyFailure 2>&1 | Out-String }
        }
    }
    finally {
        if ($null -eq $previous) { Remove-Item Env:\SIGNING_PROVIDER -ErrorAction SilentlyContinue }
        else { [Environment]::SetEnvironmentVariable('SIGNING_PROVIDER', $previous) }
    }
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
        $output = Invoke-TestMockSigner $exe 'MockSign'
        Assert-True ($LASTEXITCODE -eq 0) "mock signer must exit 0, got $LASTEXITCODE`n$output"
        $postHash = Get-FileSha256Lower $exe
        Assert-True ($postHash -ne $preHash) 'mock signing must change the artifact bytes like real signing does'
        $result = $output | ConvertFrom-Json
        Assert-True ($result.Status -eq 'SIGNED') 'mock signer must report SIGNED'
        Assert-True ($result.Verification -eq 'SIGNATURE_VERIFIED') 'mock signer must report SIGNATURE_VERIFIED'
        Assert-True ($result.Mock -eq $true) 'mock signer must mark the result Mock=true'
        Assert-True ($result.FinalSha256 -eq $postHash) 'mock signer FinalSha256 must be the actual post-signature hash'
        Assert-True ($result.Provider -eq 'mock-test') 'mock signer must report Provider=mock-test and never an approved provider'
        Assert-True ($result.KeyProtection -eq 'MOCK_TEST') 'mock signer must report KeyProtection=MOCK_TEST and never CLOUD_HSM'
        Assert-True ($result.TimestampStatus -eq 'NOT_PERFORMED') 'mock signer must never claim a real RFC3161 timestamp'
    }

    New-TestCase 'mock-sign-then-finalize-records-end-to-end' {
        # The integration seam between sign-release.ps1's output contract and
        # the record finalization: the returned FinalSha256 drives the final
        # hash everywhere.
        $exe = Join-Path $testRoot 't-mock-sign-finalize.exe'
        New-DummyArtifact $exe
        $unsignedHash = Get-FileSha256Lower $exe
        $output = Invoke-TestMockSigner $exe 'MockSign'
        Assert-True ($LASTEXITCODE -eq 0) "mock signer failed: $output"
        $result = $output | ConvertFrom-Json
        $dir = Join-Path $testRoot 't-mock-sign-finalize'
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        $manifest = New-TestManifest -UnsignedSha $unsignedHash -FinalSignedSha $result.FinalSha256 `
            -SigningStatus 'SIGNED' -SignatureVerification 'SIGNATURE_VERIFIED' -Mock $true `
            -Provider 'mock-test' -KeyProtection 'MOCK_TEST'
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
        $output = Invoke-TestMockSigner $exe 'MockSignFailure'
        Assert-True ($LASTEXITCODE -eq 3) "mock sign failure must exit 3, got $LASTEXITCODE"
        $result = $output | ConvertFrom-Json
        Assert-True ($result.Status -eq 'SIGNING_FAILED') 'mock sign failure must report SIGNING_FAILED'
        Assert-True ((Get-FileSha256Lower $exe) -eq $preHash) 'a failed sign must not mutate the artifact'
    }

    New-TestCase 'mock-verify-failure-exits-nonzero' {
        $exe = Join-Path $testRoot 't-mock-verify-failure.exe'
        New-DummyArtifact $exe
        $output = Invoke-TestMockSigner $exe 'MockVerifyFailure'
        Assert-True ($LASTEXITCODE -eq 3) "mock verify failure must exit 3, got $LASTEXITCODE"
        $result = $output | ConvertFrom-Json
        Assert-True ($result.Status -eq 'SIGNED' -and $result.Verification -eq 'FAILED') 'mock verify failure must report SIGNED/FAILED'
        Assert-True ($result.Provider -eq 'mock-test') 'even a failing mock result must never claim an approved provider'
    }

    New-TestCase 'mock-modes-refuse-to-mix-with-real-material' {
        $exe = Join-Path $testRoot 't-mock-real.exe'
        New-DummyArtifact $exe
        $env:SIGNCERT_BASE64 = 'c2hvdWxkLW5ldmVyLWJlLXVzZWQ='
        try {
            $refused = $false
            try {
                $null = Invoke-TestMockSigner $exe 'MockSign'
            }
            catch {
                $refused = $true
            }
            Assert-True $refused 'mock mode must refuse to run while real provider material is present'
        }
        finally {
            Remove-Item Env:\SIGNCERT_BASE64 -ErrorAction SilentlyContinue
        }
    }

    New-TestCase 'mock-modes-refuse-without-explicit-mock-provider' {
        # The mock provider must be selected EXPLICITLY (SIGNING_PROVIDER=
        # mock-test): a mock flag without the explicit mock provider can never
        # run, so production configurations can never accidentally invoke it.
        $exe = Join-Path $testRoot 't-mock-no-provider.exe'
        New-DummyArtifact $exe
        $previous = [Environment]::GetEnvironmentVariable('SIGNING_PROVIDER')
        [Environment]::SetEnvironmentVariable('SIGNING_PROVIDER', '')
        try {
            $refused = $false
            try {
                & $signScript -ExePath $exe -MockSign 2>&1 | Out-Null
            }
            catch {
                $refused = $true
            }
            Assert-True $refused 'mock mode without SIGNING_PROVIDER=mock-test must be refused'
        }
        finally {
            if ($null -eq $previous) { Remove-Item Env:\SIGNING_PROVIDER -ErrorAction SilentlyContinue }
            else { [Environment]::SetEnvironmentVariable('SIGNING_PROVIDER', $previous) }
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
    # The CURRENT trusted publisher policy used by the gate tests: must equal
    # the manifest signingCertificateSubject set by Set-SyntheticSignedManifest.
    $goodPublisherSubject = 'CN=TabDock Test Publisher, O=TabDock Test, C=US'

    New-TestCase 'unsigned-artifact-rejected-when-production-signing-mandatory' {
        $art = New-SyntheticArtifactDir $testRoot 'g-unsigned-mandatory' -SourceSha $goodSha -Version $goodVersion
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -RequireSigning -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
        Assert-True (-not $gate.Eligible) 'unsigned artifact must be ineligible under mandatory production signing'
        Assert-True (($gate.Failures -join ';') -match 'signing') 'failure must identify the signing policy'
    }

    New-TestCase 'manifest-signing-failure-state-rejected' {
        $art = New-SyntheticArtifactDir $testRoot 'g-signing-failed' -SourceSha $goodSha -Version $goodVersion
        Update-TestManifest $art.ManifestPath @{ signingStatus = 'SIGNING_FAILED'; signatureVerification = 'FAILED' }
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -RequireSigning -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
        Assert-True (-not $gate.Eligible) 'a SIGNING_FAILED manifest must never be production-eligible'
    }

    New-TestCase 'signature-verification-failure-rejected' {
        $art = New-SyntheticArtifactDir $testRoot 'g-verify-failed' -SourceSha $goodSha -Version $goodVersion
        Set-SyntheticSignedManifest $art.ManifestPath $art.ArtifactSha256
        Update-TestManifest $art.ManifestPath @{ signatureVerification = 'FAILED' }
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -RequireSigning -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
        Assert-True (-not $gate.Eligible) 'an unverified signature must never be treated as valid for production'
        Assert-True (($gate.Failures -join ';') -match 'signatureVerification') 'failure must cite signature verification'
    }

    New-TestCase 'mock-signed-artifact-rejected-for-production' {
        $art = New-SyntheticArtifactDir $testRoot 'g-mock' -SourceSha $goodSha -Version $goodVersion
        Set-SyntheticSignedManifest $art.ManifestPath $art.ArtifactSha256
        Update-TestManifest $art.ManifestPath @{ signingMock = $true; signingProvider = 'mock-test'; signingKeyProtection = 'MOCK_TEST' }
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -RequireSigning -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
        Assert-True (-not $gate.Eligible) 'a test-only mock-signed artifact must never be production-eligible'
        Assert-True (($gate.Failures -join ';') -match 'mock') 'failure must identify mock signing'
    }

    New-TestCase 'missing-external-evidence-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'g-missing-evidence' -SourceSha $goodSha -Version $goodVersion
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath (Join-Path $art.Dir 'does-not-exist.json') -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
        Assert-True (-not $gate.Eligible) 'missing external evidence must fail closed'
    }

    New-TestCase 'malformed-external-evidence-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'g-malformed-evidence' -SourceSha $goodSha -Version $goodVersion
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        [IO.File]::WriteAllText($evidencePath, '{ not json', [Text.UTF8Encoding]::new($false))
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
        Assert-True (-not $gate.Eligible) 'malformed external evidence must fail closed'
    }

    New-TestCase 'evidence-from-wrong-source-sha-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'g-wrong-sha' -SourceSha $goodSha -Version $goodVersion
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha ('d' * 40) -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
        Assert-True (-not $gate.Eligible) 'evidence bound to another source SHA must fail closed'
        Assert-True (($gate.Failures -join ';') -match 'sourceCommitSha') 'failure must cite the SHA binding'
    }

    New-TestCase 'evidence-from-wrong-artifact-hash-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'g-wrong-hash' -SourceSha $goodSha -Version $goodVersion
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha ('e' * 64))
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
        Assert-True (-not $gate.Eligible) 'evidence bound to another artifact must fail closed'
        Assert-True (($gate.Failures -join ';') -match 'artifactSha256') 'failure must cite the artifact binding'
    }

    New-TestCase 'smoke-fail-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'g-smoke-fail' -SourceSha $goodSha -Version $goodVersion
        $evidence = New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256
        $evidence.finalWindowsHumanSmoke.status = 'FAIL'
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath $evidence
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
        Assert-True (-not $gate.Eligible) 'finalWindowsHumanSmoke=FAIL must fail closed'
    }

    New-TestCase 'smoke-blocked-external-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'g-smoke-blocked' -SourceSha $goodSha -Version $goodVersion
        $evidence = New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256
        $evidence.finalWindowsHumanSmoke.status = 'BLOCKED_EXTERNAL'
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath $evidence
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
        Assert-True (-not $gate.Eligible) 'finalWindowsHumanSmoke=BLOCKED_EXTERNAL must fail closed'
    }

    New-TestCase 'dpi-fail-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'g-dpi-fail' -SourceSha $goodSha -Version $goodVersion
        $evidence = New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256
        $evidence.physicalMixedDpi.status = 'FAIL'
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath $evidence
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
        Assert-True (-not $gate.Eligible) 'physicalMixedDpi=FAIL must fail closed'
    }

    New-TestCase 'dpi-blocked-external-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'g-dpi-blocked' -SourceSha $goodSha -Version $goodVersion
        $evidence = New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256
        $evidence.physicalMixedDpi.status = 'BLOCKED_NO_MIXED_DPI_HARDWARE'
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath $evidence
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
        Assert-True (-not $gate.Eligible) 'physicalMixedDpi=BLOCKED_NO_MIXED_DPI_HARDWARE must fail closed for production'
    }

    New-TestCase 'evidence-missing-gate-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'g-missing-gate' -SourceSha $goodSha -Version $goodVersion
        $evidence = New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256
        $evidence.Remove('physicalMixedDpi')
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath $evidence
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
        Assert-True (-not $gate.Eligible) 'evidence without a mandatory gate must fail closed'
    }

    New-TestCase 'evidence-missing-operator-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'g-no-operator' -SourceSha $goodSha -Version $goodVersion
        $evidence = New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256
        $evidence.finalWindowsHumanSmoke.operator = ''
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath $evidence
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
        Assert-True (-not $gate.Eligible) 'evidence without an operator must fail closed'
    }

    New-TestCase 'evidence-wrong-schema-version-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'g-schema' -SourceSha $goodSha -Version $goodVersion
        $evidence = New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256
        $evidence.schemaVersion = 99
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath $evidence
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
        Assert-True (-not $gate.Eligible) 'wrong evidence schema version must fail closed'
    }

    New-TestCase 'manifest-hash-mismatch-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'g-manifest-hash' -SourceSha $goodSha -Version $goodVersion
        Update-TestManifest $art.ManifestPath @{ artifactSha256 = ('f' * 64) }
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
        Assert-True (-not $gate.Eligible) 'manifest hash disagreement must fail closed'
    }

    New-TestCase 'sha256sums-mismatch-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'g-sums' -SourceSha $goodSha -Version $goodVersion
        [IO.File]::WriteAllText($art.SumsPath, ('0' * 64) + "  TabDock.exe`r`n", [Text.UTF8Encoding]::new($false))
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
        Assert-True (-not $gate.Eligible) 'SHA256SUMS disagreement must fail closed'
    }

    New-TestCase 'final-executable-tampered-after-qualification-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'g-tampered' -SourceSha $goodSha -Version $goodVersion
        $bytes = [IO.File]::ReadAllBytes($art.Exe)
        $bytes[0] = $bytes[0] -bxor 0xFF
        [IO.File]::WriteAllBytes($art.Exe, $bytes)
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
        Assert-True (-not $gate.Eligible) 'a tampered final executable must fail closed'
    }

    New-TestCase 'wrong-semantic-version-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'g-version' -SourceSha $goodSha -Version $goodVersion
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion '9.9.9' -EvidencePath $evidencePath -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
        Assert-True (-not $gate.Eligible) 'a version disagreement must fail closed'
    }

    New-TestCase 'wrong-candidate-sha-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'g-sha' -SourceSha $goodSha -Version $goodVersion
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha ('c' * 40) -ExpectedVersion $goodVersion -EvidencePath $evidencePath -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
        Assert-True (-not $gate.Eligible) 'a requested-SHA disagreement must fail closed'
    }

    New-TestCase 'failed-qualification-manifest-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'g-qual-failed' -SourceSha $goodSha -Version $goodVersion
        Update-TestManifest $art.ManifestPath @{ qualificationStatus = 'FAIL' }
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
        Assert-True (-not $gate.Eligible) 'a FAIL manifest must fail closed'
    }

    Write-Host ''
    Write-Host '==> Publication gate: happy paths' -ForegroundColor Cyan

    New-TestCase 'production-eligible-synthetic-happy-path' {
        $art = New-SyntheticArtifactDir $testRoot 'h-production' -SourceSha $goodSha -Version $goodVersion
        Set-SyntheticSignedManifest $art.ManifestPath $art.ArtifactSha256
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        # The synthetic artifact is not really Authenticode-signed, so the
        # independent signtool re-verification is exercised as the signing
        # branch; policy-declared signing is covered by the negative cases.
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
        Assert-True ($gate.Eligible) "production-eligible synthetic records must pass all non-signing gates: $($gate.Failures -join '; ')"
        Assert-True ($gate.Failures.Count -eq 0) 'no failures expected on the synthetic happy path'
    }

    New-TestCase 'mandatory-signing-adds-exactly-the-signing-failure' {
        # Decomposition: with RequireSigning, the ONLY added failure must be
        # the independent Authenticode verification (the test artifact is
        # intentionally unsigned). This proves the gate does not hide other
        # defects behind the signing branch - the approved-provider manifest
        # metadata, certificate identity, and timestamp policy all pass.
        $art = New-SyntheticArtifactDir $testRoot 'h-mandatory' -SourceSha $goodSha -Version $goodVersion
        Set-SyntheticSignedManifest $art.ManifestPath $art.ArtifactSha256
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -RequireSigning -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
        Assert-True (-not $gate.Eligible) 'unsigned synthetic artifact must fail mandatory signing'
        Assert-True ($gate.Failures.Count -eq 1) "expected exactly one signing failure, got: $($gate.Failures -join '; ')"
        Assert-True (($gate.Failures[0] -match 'Authenticode') -or ($gate.Failures[0] -match 'signtool')) 'the single failure must be the Authenticode re-verification'
    }

    New-TestCase 'evidence-direct-validation-binds-sha-and-hash' {
        $evidencePath = Join-Path $testRoot 'h-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha ('b' * 64))
        $result = Test-ExternalEvidenceFile $evidencePath $goodSha ('b' * 64) -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactNamegoodPublisherSubject
        Assert-True ($result.Valid) "valid evidence must pass: $($result.Failures -join '; ')"
        $wrong = Test-ExternalEvidenceFile $evidencePath $goodSha ('c' * 64) -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactNamegoodPublisherSubject
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
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion '9.9' -EvidencePath $evidencePath -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
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
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
        Assert-True (-not $gate.Eligible) 'a forged binary semantic version must fail closed'
        Assert-True (($gate.Failures -join ';') -match 'buildIdentity.semanticVersion') 'failure must cite the binary version binding'
    }

    New-TestCase 'forged-informational-version-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'v-info-forge' -SourceSha $goodSha -Version $goodVersion
        Update-TestManifest $art.ManifestPath @{ buildIdentity = [ordered]@{ semanticVersion = '1.0.0'; informationalVersion = '2.0.0+abcdef1'; selfReportedSha256 = 'unavailable' } }
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
        Assert-True (-not $gate.Eligible) 'an informational version without the manifest semantic version must fail closed'
        Assert-True (($gate.Failures -join ';') -match 'informationalVersion') 'failure must cite the informational version binding'
    }

    New-TestCase 'rc-qualification-only-artifact-rejected-for-production' {
        $art = New-SyntheticArtifactDir $testRoot 'v-rc-mode' -SourceSha $goodSha -Version $goodVersion -ReleaseMode 'QUALIFICATION_ONLY'
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
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
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
        Assert-True (-not $gate.Eligible) 'evidence naming another Stage A run must fail closed'
        Assert-True (($gate.Failures -join ';') -match 'candidateWorkflowRunId') 'failure must cite the run binding'
    }

    New-TestCase 'manifest-workflow-run-mismatch-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'r-manifest-run' -SourceSha $goodSha -Version $goodVersion -WorkflowRunId '555555555'
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
        Assert-True (-not $gate.Eligible) 'a manifest produced by another run must fail closed'
        Assert-True (($gate.Failures -join ';') -match 'workflowRunId') 'failure must cite the manifest run binding'
    }

    New-TestCase 'candidate-artifact-name-binding-required' {
        $art = New-SyntheticArtifactDir $testRoot 'r-wrong-name' -SourceSha $goodSha -Version $goodVersion
        $evidence = New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256
        $evidence.candidateArtifactName = 'tabdock-candidate-' + ('c' * 40) + '-123456789'
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath $evidence
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
        Assert-True (-not $gate.Eligible) 'evidence naming another artifact must fail closed'
        Assert-True (($gate.Failures -join ';') -match 'candidateArtifactName') 'failure must cite the artifact-name binding'
    }

    New-TestCase 'malformed-candidate-run-id-fails-closed' {
        $art = New-SyntheticArtifactDir $testRoot 'r-malformed-run' -SourceSha $goodSha -Version $goodVersion
        $evidence = New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256
        $evidence.candidateWorkflowRunId = 'not-a-number'
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath $evidence
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
        Assert-True (-not $gate.Eligible) 'a malformed candidate run id must fail closed'
    }

    New-TestCase 'missing-artifact-directory-fails-closed' {
        $gate = Test-PublicationEligibility -ArtifactDir (Join-Path $testRoot 'does-not-exist') -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath (Join-Path $testRoot 'nope.json') -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
        Assert-True (-not $gate.Eligible) 'a missing/expired artifact directory must fail closed'
        Assert-True (($gate.Failures -join ';') -match 'final artifact missing') 'failure must cite the missing artifact'
    }

    New-TestCase 'existing-valid-signed-candidate-accepted-by-non-network-gate' {
        # The Stage B gate accepts a fully consistent production candidate
        # (PRODUCTION mode, run-bound manifest, approved-provider signing
        # provenance, schema-v2 evidence) without any network; the only
        # non-signing gate condition is the independent Authenticode check,
        # which is exercised with RequireSigning.
        $art = New-SyntheticArtifactDir $testRoot 'r-happy' -SourceSha $goodSha -Version $goodVersion
        Set-SyntheticSignedManifest $art.ManifestPath $art.ArtifactSha256
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
        Assert-True ($gate.Eligible) "a fully bound production candidate must pass the non-network gate: $($gate.Failures -join '; ')"
    }

    Write-Host ''
    Write-Host '==> Windows compatibility gate (Windows 10 + Windows 11 evidence)' -ForegroundColor Cyan

    New-TestCase 'windows-compatibility-missing-fails-closed' {
        $evidence = New-TestEvidence -SourceSha $goodSha -ArtifactSha ('b' * 64)
        $evidence.Remove('windowsCompatibility')
        $evidencePath = Join-Path $testRoot 'w-missing.json'
        Save-TestEvidence $evidencePath $evidence
        $result = Test-ExternalEvidenceFile $evidencePath $goodSha ('b' * 64) -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactNamegoodPublisherSubject
        Assert-True (-not $result.Valid) 'evidence without windowsCompatibility must fail closed'
        Assert-True (($result.Failures -join ';') -match 'windowsCompatibility') 'failure must cite the missing gate'
    }

    New-TestCase 'windows-compatibility-status-fail-fails-closed' {
        $evidence = New-TestEvidence -SourceSha $goodSha -ArtifactSha ('b' * 64)
        $evidence.windowsCompatibility.status = 'FAIL'
        $evidencePath = Join-Path $testRoot 'w-fail.json'
        Save-TestEvidence $evidencePath $evidence
        $result = Test-ExternalEvidenceFile $evidencePath $goodSha ('b' * 64) -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactNamegoodPublisherSubject
        Assert-True (-not $result.Valid) 'windowsCompatibility status FAIL must fail closed'
    }

    New-TestCase 'windows10-missing-fails-closed' {
        $evidence = New-TestEvidence -SourceSha $goodSha -ArtifactSha ('b' * 64)
        $evidence.windowsCompatibility.Remove('windows10')
        $evidencePath = Join-Path $testRoot 'w-no10.json'
        Save-TestEvidence $evidencePath $evidence
        $result = Test-ExternalEvidenceFile $evidencePath $goodSha ('b' * 64) -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactNamegoodPublisherSubject
        Assert-True (-not $result.Valid) 'evidence without Windows 10 entry must fail closed'
        Assert-True (($result.Failures -join ';') -match 'windows10') 'failure must cite the Windows 10 entry'
    }

    New-TestCase 'windows10-status-blocked-fails-closed' {
        $evidence = New-TestEvidence -SourceSha $goodSha -ArtifactSha ('b' * 64)
        $evidence.windowsCompatibility.windows10.status = 'BLOCKED_EXTERNAL'
        $evidencePath = Join-Path $testRoot 'w-10blocked.json'
        Save-TestEvidence $evidencePath $evidence
        $result = Test-ExternalEvidenceFile $evidencePath $goodSha ('b' * 64) -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactNamegoodPublisherSubject
        Assert-True (-not $result.Valid) 'Windows 10 BLOCKED_EXTERNAL must fail closed for production'
    }

    New-TestCase 'windows10-missing-build-fails-closed' {
        $evidence = New-TestEvidence -SourceSha $goodSha -ArtifactSha ('b' * 64)
        $evidence.windowsCompatibility.windows10.build = ''
        $evidencePath = Join-Path $testRoot 'w-10build.json'
        Save-TestEvidence $evidencePath $evidence
        $result = Test-ExternalEvidenceFile $evidencePath $goodSha ('b' * 64) -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactNamegoodPublisherSubject
        Assert-True (-not $result.Valid) 'Windows 10 evidence without a recorded build must fail closed'
        Assert-True (($result.Failures -join ';') -match 'build') 'failure must cite the missing OS build'
    }

    New-TestCase 'windows10-missing-native-abi-evidence-fails-closed' {
        $evidence = New-TestEvidence -SourceSha $goodSha -ArtifactSha ('b' * 64)
        $evidence.windowsCompatibility.windows10.nativeAbiEvidence = ''
        $evidencePath = Join-Path $testRoot 'w-10abi.json'
        Save-TestEvidence $evidencePath $evidence
        $result = Test-ExternalEvidenceFile $evidencePath $goodSha ('b' * 64) -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactNamegoodPublisherSubject
        Assert-True (-not $result.Valid) 'Windows 10 evidence without the native ABI selftest report must fail closed'
        Assert-True (($result.Failures -join ';') -match 'nativeAbiEvidence') 'failure must cite the native ABI evidence'
    }

    New-TestCase 'windows11-status-fail-fails-closed' {
        $evidence = New-TestEvidence -SourceSha $goodSha -ArtifactSha ('b' * 64)
        $evidence.windowsCompatibility.windows11.status = 'FAIL'
        $evidencePath = Join-Path $testRoot 'w-11fail.json'
        Save-TestEvidence $evidencePath $evidence
        $result = Test-ExternalEvidenceFile $evidencePath $goodSha ('b' * 64) -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactNamegoodPublisherSubject
        Assert-True (-not $result.Valid) 'Windows 11 status FAIL must fail closed'
    }

    New-TestCase 'windows-compatibility-happy-path-passes' {
        $evidencePath = Join-Path $testRoot 'w-happy.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha ('b' * 64))
        $result = Test-ExternalEvidenceFile $evidencePath $goodSha ('b' * 64) -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactNamegoodPublisherSubject
        Assert-True ($result.Valid) "valid Windows 10/11 evidence must pass: $($result.Failures -join '; ')"
    }

    Write-Host ''
    Write-Host '==> completedAt quality (ISO-8601, not in the future)' -ForegroundColor Cyan

    New-TestCase 'completed-at-not-iso-fails-closed' {
        $evidence = New-TestEvidence -SourceSha $goodSha -ArtifactSha ('b' * 64)
        $evidence.finalWindowsHumanSmoke.completedAt = 'tomorrow-ish'
        $evidencePath = Join-Path $testRoot 'c-not-iso.json'
        Save-TestEvidence $evidencePath $evidence
        $result = Test-ExternalEvidenceFile $evidencePath $goodSha ('b' * 64) -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactNamegoodPublisherSubject
        Assert-True (-not $result.Valid) 'a non-ISO completedAt must fail closed'
        Assert-True (($result.Failures -join ';') -match 'ISO-8601') 'failure must cite the timestamp format'
    }

    New-TestCase 'completed-at-in-future-fails-closed' {
        $evidence = New-TestEvidence -SourceSha $goodSha -ArtifactSha ('b' * 64)
        $evidence.physicalMixedDpi.completedAt = [DateTimeOffset]::UtcNow.AddHours(1).ToString('O')
        $evidencePath = Join-Path $testRoot 'c-future.json'
        Save-TestEvidence $evidencePath $evidence
        $result = Test-ExternalEvidenceFile $evidencePath $goodSha ('b' * 64) -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactNamegoodPublisherSubject
        Assert-True (-not $result.Valid) 'a future completedAt must fail closed'
        Assert-True (($result.Failures -join ';') -match 'future') 'failure must cite the future timestamp'
    }

    New-TestCase 'windows-compatibility-completed-at-validated' {
        $evidence = New-TestEvidence -SourceSha $goodSha -ArtifactSha ('b' * 64)
        $evidence.windowsCompatibility.windows10.completedAt = 'not-a-date'
        $evidencePath = Join-Path $testRoot 'c-wc.json'
        Save-TestEvidence $evidencePath $evidence
        $result = Test-ExternalEvidenceFile $evidencePath $goodSha ('b' * 64) -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactNamegoodPublisherSubject
        Assert-True (-not $result.Valid) 'a malformed Windows 10 completedAt must fail closed'
    }

    Write-Host ''
    Write-Host '==> Signing provider policy (production vs development signing)' -ForegroundColor Cyan

    New-TestCase 'signing-provider-policy-approves-only-cloud-hsm' {
        # The production allowlist: ONLY non-exportable-key backends qualify.
        Assert-True ((Get-ApprovedProductionSigningProviders -join ',') -eq 'digicert-stm') 'the approved production provider allowlist must be exactly digicert-stm'
        Assert-True (Test-ApprovedProductionSigningProvider 'digicert-stm') 'digicert-stm must be approved for production'
        foreach ($rejected in @('local-pfx', 'mock-test', 'not-configured', '', 'mystery-vendor')) {
            Assert-True (-not (Test-ApprovedProductionSigningProvider $rejected)) "provider '$rejected' must never be approved for production"
        }
        Assert-True (Test-ApprovedProductionKeyProtection 'CLOUD_HSM') 'CLOUD_HSM key protection must be approved'
        foreach ($rejected in @('LOCAL_PFX', 'MOCK_TEST', 'NOT_CONFIGURED', '')) {
            Assert-True (-not (Test-ApprovedProductionKeyProtection $rejected)) "key protection '$rejected' must never be approved for production"
        }
        Assert-True ((Get-SigningProviderKeyProtection 'digicert-stm') -eq 'CLOUD_HSM') 'digicert-stm must classify as CLOUD_HSM'
        Assert-True ((Get-SigningProviderKeyProtection 'local-pfx') -eq 'LOCAL_PFX') 'local-pfx must classify as LOCAL_PFX (exportable key)'
        Assert-True ((Get-SigningProviderKeyProtection 'mock-test') -eq 'MOCK_TEST') 'mock-test must classify as MOCK_TEST'
        Assert-True ((Get-SigningProviderKeyProtection 'not-configured') -eq 'NOT_CONFIGURED') 'not-configured must classify as NOT_CONFIGURED'
    }

    New-TestCase 'unknown-signing-provider-rejected' {
        $previous = [Environment]::GetEnvironmentVariable('SIGNING_PROVIDER')
        [Environment]::SetEnvironmentVariable('SIGNING_PROVIDER', 'mystery-vendor')
        try {
            Assert-Throws 'an unknown SIGNING_PROVIDER must throw (never silently fall back)' { $null = Get-SigningProvider }
        }
        finally {
            if ($null -eq $previous) { Remove-Item Env:\SIGNING_PROVIDER -ErrorAction SilentlyContinue }
            else { [Environment]::SetEnvironmentVariable('SIGNING_PROVIDER', $previous) }
        }
    }

    New-TestCase 'sign-release-reports-not-configured-without-provider' {
        $exe = Join-Path $testRoot 'p-not-configured.exe'
        New-DummyArtifact $exe
        $previous = [Environment]::GetEnvironmentVariable('SIGNING_PROVIDER')
        [Environment]::SetEnvironmentVariable('SIGNING_PROVIDER', '')
        try {
            $output = & $signScript -ExePath $exe 2>&1 | Out-String
            Assert-True ($LASTEXITCODE -eq 0) "no-provider signer must exit 0, got $LASTEXITCODE`n$output"
            $result = $output | ConvertFrom-Json
            Assert-True ($result.Status -eq 'NOT_CONFIGURED') 'no provider must report NOT_CONFIGURED'
            Assert-True ($result.Provider -eq 'not-configured') 'no provider must report Provider=not-configured'
            Assert-True ($result.KeyProtection -eq 'NOT_CONFIGURED') 'no provider must report KeyProtection=NOT_CONFIGURED'
        }
        finally {
            if ($null -eq $previous) { Remove-Item Env:\SIGNING_PROVIDER -ErrorAction SilentlyContinue }
            else { [Environment]::SetEnvironmentVariable('SIGNING_PROVIDER', $previous) }
        }
    }

    New-TestCase 'digicert-stm-configuration-incomplete-fails-closed' {
        # Cloud-provider authentication failure: without the SM_* credentials
        # the provider cannot authenticate to the signing service. The
        # preflight names ONLY the missing variable names and the signer
        # exits BLOCKED_EXTERNAL without touching the file.
        $envNames = @('SM_HOST', 'SM_API_KEY', 'SM_CLIENT_CERT_FILE', 'SM_CLIENT_CERT_PASSWORD', 'SM_KEYPAIR_ALIAS')
        $saved = @{}
        foreach ($name in $envNames) { $saved[$name] = [Environment]::GetEnvironmentVariable($name); [Environment]::SetEnvironmentVariable($name, '') }
        $previousProvider = [Environment]::GetEnvironmentVariable('SIGNING_PROVIDER')
        [Environment]::SetEnvironmentVariable('SIGNING_PROVIDER', 'digicert-stm')
        try {
            $cfg = Test-SigningProviderConfiguration 'digicert-stm'
            Assert-True (-not $cfg.Configured) 'digicert-stm without credentials must be unconfigured'
            foreach ($required in $envNames) {
                Assert-True ($cfg.Missing -contains $required) "missing list must name $required"
            }
            $exe = Join-Path $testRoot 'p-stm-nocreds.exe'
            New-DummyArtifact $exe
            $preHash = Get-FileSha256Lower $exe
            $output = & $signScript -ExePath $exe 2>&1 | Out-String
            Assert-True ($LASTEXITCODE -eq 2) "digicert-stm without credentials must exit 2 (BLOCKED_EXTERNAL), got $LASTEXITCODE`n$output"
            $result = $output | ConvertFrom-Json
            Assert-True ($result.Status -eq 'BLOCKED_EXTERNAL') 'the signer must report BLOCKED_EXTERNAL'
            Assert-True ($result.Provider -eq 'digicert-stm') 'the signer must still report the selected provider'
            Assert-True ((Get-FileSha256Lower $exe) -eq $preHash) 'a blocked provider must not mutate the artifact'
        }
        finally {
            foreach ($name in $envNames) {
                if ($null -eq $saved[$name]) { Remove-Item "Env:\$name" -ErrorAction SilentlyContinue }
                else { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
            }
            if ($null -eq $previousProvider) { Remove-Item Env:\SIGNING_PROVIDER -ErrorAction SilentlyContinue }
            else { [Environment]::SetEnvironmentVariable('SIGNING_PROVIDER', $previousProvider) }
        }
    }

    New-TestCase 'digicert-stm-configuration-complete-when-all-present' {
        $p12 = Join-Path $testRoot 'p-fake-client.p12'
        [IO.File]::WriteAllBytes($p12, [byte[]]::new(64))
        $envNames = @('SM_HOST', 'SM_API_KEY', 'SM_CLIENT_CERT_FILE', 'SM_CLIENT_CERT_PASSWORD', 'SM_KEYPAIR_ALIAS')
        $saved = @{}
        foreach ($name in $envNames) { $saved[$name] = [Environment]::GetEnvironmentVariable($name) }
        try {
            [Environment]::SetEnvironmentVariable('SM_HOST', 'https://example.invalid')
            [Environment]::SetEnvironmentVariable('SM_API_KEY', 'test-api-key')
            [Environment]::SetEnvironmentVariable('SM_CLIENT_CERT_FILE', $p12)
            [Environment]::SetEnvironmentVariable('SM_CLIENT_CERT_PASSWORD', 'test-password')
            [Environment]::SetEnvironmentVariable('SM_KEYPAIR_ALIAS', 'test-keypair')
            $cfg = Test-SigningProviderConfiguration 'digicert-stm'
            Assert-True ($cfg.Configured) "digicert-stm with all credentials must be configured: missing=$($cfg.Missing -join ',')"
            Assert-True ($cfg.Missing.Count -eq 0) 'no missing credentials expected'
        }
        finally {
            foreach ($name in $envNames) {
                if ($null -eq $saved[$name]) { Remove-Item "Env:\$name" -ErrorAction SilentlyContinue }
                else { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
            }
        }
    }

    New-TestCase 'cloud-provider-signing-tool-absent-fails-closed' {
        # A selected cloud provider whose official tooling is unavailable must
        # fail closed (SIGNING_FAILED, exit 3) - signing can never silently
        # degrade. SMCTL_PATH points at a nonexistent smctl, which also keeps
        # the case deterministic on any machine.
        $p12 = Join-Path $testRoot 'p-tool-client.p12'
        [IO.File]::WriteAllBytes($p12, [byte[]]::new(64))
        $envNames = @('SM_HOST', 'SM_API_KEY', 'SM_CLIENT_CERT_FILE', 'SM_CLIENT_CERT_PASSWORD', 'SM_KEYPAIR_ALIAS', 'SMCTL_PATH')
        $saved = @{}
        foreach ($name in $envNames) { $saved[$name] = [Environment]::GetEnvironmentVariable($name) }
        $previousProvider = [Environment]::GetEnvironmentVariable('SIGNING_PROVIDER')
        [Environment]::SetEnvironmentVariable('SIGNING_PROVIDER', 'digicert-stm')
        try {
            [Environment]::SetEnvironmentVariable('SM_HOST', 'https://example.invalid')
            [Environment]::SetEnvironmentVariable('SM_API_KEY', 'test-api-key')
            [Environment]::SetEnvironmentVariable('SM_CLIENT_CERT_FILE', $p12)
            [Environment]::SetEnvironmentVariable('SM_CLIENT_CERT_PASSWORD', 'test-password')
            [Environment]::SetEnvironmentVariable('SM_KEYPAIR_ALIAS', 'test-keypair')
            [Environment]::SetEnvironmentVariable('SMCTL_PATH', (Join-Path $testRoot 'no-such-smctl.exe'))
            $exe = Join-Path $testRoot 'p-tool-missing.exe'
            New-DummyArtifact $exe
            $preHash = Get-FileSha256Lower $exe
            $output = & $signScript -ExePath $exe 2>&1 | Out-String
            Assert-True ($LASTEXITCODE -eq 3) "missing smctl must exit 3 (SIGNING_FAILED), got $LASTEXITCODE`n$output"
            $result = $output | ConvertFrom-Json
            Assert-True ($result.Status -eq 'SIGNING_FAILED') 'missing tooling must report SIGNING_FAILED'
            Assert-True ((Get-FileSha256Lower $exe) -eq $preHash) 'a failed cloud signing attempt must not mutate the artifact'
        }
        finally {
            foreach ($name in $envNames) {
                if ($null -eq $saved[$name]) { Remove-Item "Env:\$name" -ErrorAction SilentlyContinue }
                else { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
            }
            if ($null -eq $previousProvider) { Remove-Item Env:\SIGNING_PROVIDER -ErrorAction SilentlyContinue }
            else { [Environment]::SetEnvironmentVariable('SIGNING_PROVIDER', $previousProvider) }
        }
    }

    New-TestCase 'local-pfx-configuration-valid-for-rc-not-for-production' {
        # local-pfx remains a RECOGNIZED provider for development/private/RC
        # use, but it is explicitly NOT the approved public-GA signer.
        $envNames = @('SIGNCERT_BASE64', 'SIGNCERT_PASSWORD')
        $saved = @{}
        foreach ($name in $envNames) { $saved[$name] = [Environment]::GetEnvironmentVariable($name) }
        try {
            [Environment]::SetEnvironmentVariable('SIGNCERT_BASE64', 'c2hvdWxkLW5ldmVyLWJlLXVzZWQ=')
            [Environment]::SetEnvironmentVariable('SIGNCERT_PASSWORD', 'not-a-real-password')
            $cfg = Test-SigningProviderConfiguration 'local-pfx'
            Assert-True ($cfg.Configured) "local-pfx with both credentials must be configured: missing=$($cfg.Missing -join ',')"
            Assert-True ($cfg.KeyProtection -eq 'LOCAL_PFX') 'local-pfx must classify as LOCAL_PFX'
            Assert-True (-not (Test-ApprovedProductionSigningProvider 'local-pfx')) 'local-pfx must never be an approved production provider'
            Assert-True (-not (Test-ApprovedProductionKeyProtection 'LOCAL_PFX')) 'LOCAL_PFX key protection must never satisfy production'
        }
        finally {
            foreach ($name in $envNames) {
                if ($null -eq $saved[$name]) { Remove-Item "Env:\$name" -ErrorAction SilentlyContinue }
                else { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
            }
        }
    }

    New-TestCase 'unsigned-not-configured-manifest-allowed-when-signing-not-required' {
        # RC qualification may stay NOT_CONFIGURED: the gate without
        # RequireSigning accepts a not-configured unsigned manifest (Stage B
        # always passes -RequireSigning; the switch is what makes signing
        # mandatory, mirroring release-qualify's RELEASE_SIGNING_REQUIRED).
        $art = New-SyntheticArtifactDir $testRoot 'p-rc-unsigned' -SourceSha $goodSha -Version $goodVersion
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
        Assert-True ($gate.Eligible) "an unsigned not-configured record must pass the non-signing gate: $($gate.Failures -join '; ')"
        # The same record must fail when production signing is mandatory.
        $mandatory = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -RequireSigning -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
        Assert-True (-not $mandatory.Eligible) 'the same unsigned record must fail under mandatory production signing'
    }

    New-TestCase 'production-rejects-local-pfx-provider' {
        $art = New-SyntheticArtifactDir $testRoot 'p-provider-pfx' -SourceSha $goodSha -Version $goodVersion
        Set-SyntheticSignedManifest $art.ManifestPath $art.ArtifactSha256
        Update-TestManifest $art.ManifestPath @{ signingProvider = 'local-pfx'; signingKeyProtection = 'LOCAL_PFX' }
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -RequireSigning -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
        Assert-True (-not $gate.Eligible) 'a local-PFX-signed artifact must never be production-eligible'
        Assert-True (($gate.Failures -join ';') -match 'local-pfx') 'failure must identify the local-PFX provider'
    }

    New-TestCase 'production-rejects-not-configured-provider' {
        $art = New-SyntheticArtifactDir $testRoot 'p-provider-none' -SourceSha $goodSha -Version $goodVersion
        Set-SyntheticSignedManifest $art.ManifestPath $art.ArtifactSha256
        Update-TestManifest $art.ManifestPath @{ signingProvider = 'not-configured'; signingKeyProtection = 'NOT_CONFIGURED' }
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -RequireSigning -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
        Assert-True (-not $gate.Eligible) 'an unconfigured signer must never be production-eligible'
        Assert-True (($gate.Failures -join ';') -match 'signingProvider') 'failure must cite the provider classification'
    }

    New-TestCase 'production-rejects-mock-provider' {
        $art = New-SyntheticArtifactDir $testRoot 'p-provider-mock' -SourceSha $goodSha -Version $goodVersion
        Set-SyntheticSignedManifest $art.ManifestPath $art.ArtifactSha256
        Update-TestManifest $art.ManifestPath @{ signingProvider = 'mock-test'; signingKeyProtection = 'MOCK_TEST' }
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -RequireSigning -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
        Assert-True (-not $gate.Eligible) 'a mock-provider manifest must never be production-eligible'
        Assert-True (($gate.Failures -join ';') -match 'mock') 'failure must identify the mock provider'
    }

    New-TestCase 'production-rejects-unknown-provider' {
        $art = New-SyntheticArtifactDir $testRoot 'p-provider-unknown' -SourceSha $goodSha -Version $goodVersion
        Set-SyntheticSignedManifest $art.ManifestPath $art.ArtifactSha256
        Update-TestManifest $art.ManifestPath @{ signingProvider = 'mystery-vendor'; signingKeyProtection = 'CLOUD_HSM' }
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -RequireSigning -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
        Assert-True (-not $gate.Eligible) 'an unknown signing provider must never be production-eligible'
        Assert-True (($gate.Failures -join ';') -match 'mystery-vendor') 'failure must cite the unknown provider'
    }

    New-TestCase 'production-rejects-missing-provider-metadata' {
        $art = New-SyntheticArtifactDir $testRoot 'p-provider-missing' -SourceSha $goodSha -Version $goodVersion
        Set-SyntheticSignedManifest $art.ManifestPath $art.ArtifactSha256
        Update-TestManifest $art.ManifestPath @{ signingProvider = $null }
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -RequireSigning -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
        Assert-True (-not $gate.Eligible) 'a manifest without signingProvider must fail closed'
        Assert-True (($gate.Failures -join ';') -match 'signingProvider') 'failure must cite the missing provider metadata'
    }

    New-TestCase 'production-rejects-missing-key-protection-metadata' {
        $art = New-SyntheticArtifactDir $testRoot 'p-protection-missing' -SourceSha $goodSha -Version $goodVersion
        Set-SyntheticSignedManifest $art.ManifestPath $art.ArtifactSha256
        Update-TestManifest $art.ManifestPath @{ signingKeyProtection = $null }
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -RequireSigning -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
        Assert-True (-not $gate.Eligible) 'a manifest without signingKeyProtection must fail closed'
        Assert-True (($gate.Failures -join ';') -match 'signingKeyProtection') 'failure must cite the missing key-protection metadata'
    }

    New-TestCase 'production-rejects-local-pfx-key-protection' {
        # An approved provider with an EXPORTABLE key classification is still
        # rejected: CLOUD_HSM-class protection is required.
        $art = New-SyntheticArtifactDir $testRoot 'p-protection-pfx' -SourceSha $goodSha -Version $goodVersion
        Set-SyntheticSignedManifest $art.ManifestPath $art.ArtifactSha256
        Update-TestManifest $art.ManifestPath @{ signingKeyProtection = 'LOCAL_PFX' }
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -RequireSigning -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
        Assert-True (-not $gate.Eligible) 'LOCAL_PFX key protection must never satisfy production even with an approved provider name'
        Assert-True (($gate.Failures -join ';') -match 'signingKeyProtection') 'failure must cite the key-protection class'
    }

    New-TestCase 'production-manifest-cannot-claim-cloud-hsm-while-mock-used' {
        # A manifest claiming CLOUD_HSM while mock mode was used is a
        # contradiction and must fail closed on the mock marker.
        $art = New-SyntheticArtifactDir $testRoot 'p-mock-claims-hsm' -SourceSha $goodSha -Version $goodVersion
        Set-SyntheticSignedManifest $art.ManifestPath $art.ArtifactSha256
        Update-TestManifest $art.ManifestPath @{ signingMock = $true }
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -RequireSigning -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
        Assert-True (-not $gate.Eligible) 'a manifest that claims CLOUD_HSM while recording mock signing must fail closed'
        Assert-True (($gate.Failures -join ';') -match 'mock') 'failure must identify mock signing'
    }

    New-TestCase 'production-rejects-unverified-timestamp' {
        $art = New-SyntheticArtifactDir $testRoot 'p-timestamp' -SourceSha $goodSha -Version $goodVersion
        Set-SyntheticSignedManifest $art.ManifestPath $art.ArtifactSha256
        Update-TestManifest $art.ManifestPath @{ timestampStatus = 'FAILED' }
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -RequireSigning -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
        Assert-True (-not $gate.Eligible) 'a failed/unverified timestamp must block production when timestamp policy is mandatory'
        Assert-True (($gate.Failures -join ';') -match 'timestampStatus') 'failure must cite the timestamp status'
    }

    New-TestCase 'production-rejects-missing-certificate-identity' {
        $art = New-SyntheticArtifactDir $testRoot 'p-cert-missing' -SourceSha $goodSha -Version $goodVersion
        Set-SyntheticSignedManifest $art.ManifestPath $art.ArtifactSha256
        Update-TestManifest $art.ManifestPath @{ signingCertificateThumbprint = $null }
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -RequireSigning -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
        Assert-True (-not $gate.Eligible) 'a manifest without the signed-certificate identity must fail closed'
        Assert-True (($gate.Failures -join ';') -match 'signingCertificateThumbprint') 'failure must cite the missing certificate identity'
    }

    New-TestCase 'production-rejects-certificate-without-code-signing-eku' {
        $art = New-SyntheticArtifactDir $testRoot 'p-cert-eku' -SourceSha $goodSha -Version $goodVersion
        Set-SyntheticSignedManifest $art.ManifestPath $art.ArtifactSha256
        Update-TestManifest $art.ManifestPath @{ signingCertificateEku = @('1.3.6.1.5.5.7.3.2') }
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -RequireSigning -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
        Assert-True (-not $gate.Eligible) 'a certificate without the code-signing EKU must fail closed'
        Assert-True (($gate.Failures -join ';') -match '1.3.6.1.5.5.7.3.3') 'failure must cite the code-signing EKU requirement'
    }

    New-TestCase 'production-rejects-inverted-certificate-validity-window' {
        $art = New-SyntheticArtifactDir $testRoot 'p-cert-window' -SourceSha $goodSha -Version $goodVersion
        Set-SyntheticSignedManifest $art.ManifestPath $art.ArtifactSha256
        Update-TestManifest $art.ManifestPath @{ signingCertificateValidFrom = '2029-01-01T00:00:00Z'; signingCertificateValidTo = '2026-01-01T00:00:00Z' }
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -RequireSigning -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
        Assert-True (-not $gate.Eligible) 'an inverted certificate validity window must fail closed'
        Assert-True (($gate.Failures -join ';') -match 'not before') 'failure must cite the validity window ordering'
    }

    New-TestCase 'timestamp-check-never-faked-on-unsigned-artifact' {
        # The RFC3161 timestamp gate must fail closed on an unsigned test
        # artifact: passing /tr is never treated as proof of timestamping.
        $exe = Join-Path $testRoot 'p-unsigned-ts.exe'
        New-DummyArtifact $exe
        Assert-True (-not (Test-AuthenticodeTimestamp $exe)) 'unsigned artifacts must fail timestamp verification (fail closed)'
    }

    New-TestCase 'certificate-info-never-faked-on-unsigned-artifact' {
        $exe = Join-Path $testRoot 'p-unsigned-cert.exe'
        New-DummyArtifact $exe
        Assert-True ($null -eq (Get-SignerCertificateInfo $exe)) 'unsigned artifacts must yield no certificate identity (fail closed)'
    }

    Write-Host ''
    Write-Host '==> Release-policy trust boundary and schema contract (P0/P1)' -ForegroundColor Cyan

    New-TestCase 'old-pre-hsm-production-candidate-is-rejected-by-current-policy' {
        # Adversarial model of the 51f7001-era production candidate: it looks
        # valid under its OWN old policy (releaseMode PRODUCTION, SIGNED,
        # SIGNATURE_VERIFIED, finalSignedSha256 present, no mock, valid
        # external evidence, valid hash) but carries NO current HSM provider
        # metadata and NO release-policy schema. Under the CURRENT policy this
        # shape must fail: an old candidate does not become valid merely
        # because its old policy would have accepted itself.
        $art = New-SyntheticArtifactDir $testRoot 'tb-old-pre-hsm' -SourceSha $goodSha -Version $goodVersion
        Update-TestManifest $art.ManifestPath @{
            signingStatus         = 'SIGNED'
            signatureVerification = 'SIGNATURE_VERIFIED'
            finalSignedSha256     = $art.ArtifactSha256
            signingProvider       = $null
            signingKeyProtection  = $null
            timestampStatus       = 'NOT_PERFORMED'
            signingCertificateSubject = $null
            signingCertificateThumbprint = $null
            signingCertificateIssuer = $null
            signingCertificateValidFrom = $null
            signingCertificateValidTo = $null
            signingCertificateEku = $null
            timestampCertificateSubject = $null
            timestampCertificateThumbprint = $null
            releasePolicySchemaVersion = $null
        }
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -RequireSigning -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
        Assert-True (-not $gate.Eligible) 'the old pre-HSM candidate shape must be rejected by the CURRENT production policy'
        Assert-True (($gate.Failures -join ';') -match 'signingProvider') 'failure must cite the missing current HSM provider metadata'
        Assert-True (($gate.Failures -join ';') -match 'releasePolicySchemaVersion') 'failure must cite the missing policy schema contract'
        Assert-True (($gate.Failures -join ';') -match 'publisher policy') 'failure must cite the current publisher policy'
    }

    New-TestCase 'candidate-policy-module-is-never-loaded-by-stage-b' {
        # Static policy-isolation guarantee: Stage B dot-sources the release
        # policy module EXCLUSIVELY from the trusted policy checkout
        # (policy/scripts/release-tooling.ps1). No file from
        # candidate-source/scripts or candidate-artifact/ is ever imported.
        $yml = [IO.File]::ReadAllText((Join-Path $repoRoot '.github\workflows\publish-release.yml'))
        Assert-True ($yml -match 'policy/scripts/release-tooling.ps1') 'Stage B must load the trusted policy module from policy/'
        Assert-True ($yml -notmatch [regex]::Escape('candidate-source/scripts')) 'Stage B must never reference candidate-source/scripts'
        Assert-True ($yml -notmatch [regex]::Escape('candidate-artifact/scripts')) 'Stage B must never reference candidate-artifact/scripts'
        Assert-True ($yml -notmatch [regex]::Escape("`$env:GITHUB_WORKSPACE 'scripts/release-tooling.ps1'")) 'Stage B must never dot-source the workspace-root scripts (the old candidate-controlled pattern)'
    }

    New-TestCase 'malicious-lax-candidate-release-tooling-cannot-change-stage-b-verdict' {
        # A hostile/lax candidate tree (scripts that would redefine the gate to
        # return ELIGIBLE) must never change the verdict: Stage B never loads
        # anything from the candidate. The behavioral proof: with a malicious
        # release-tooling.ps1 planted inside the artifact and source dirs, the
        # trusted gate function produces exactly the same verdict as without
        # it (unsigned artifact under mandatory signing -> ineligible with
        # exactly the Authenticode failure).
        $art = New-SyntheticArtifactDir $testRoot 'tb-malicious' -SourceSha $goodSha -Version $goodVersion
        Set-SyntheticSignedManifest $art.ManifestPath $art.ArtifactSha256
        # Plant hostile policy files exactly where the OLD vulnerable Stage B
        # would have dot-sourced them.
        New-Item -ItemType Directory -Path (Join-Path $art.Dir 'scripts') -Force | Out-Null
        [IO.File]::WriteAllText((Join-Path $art.Dir 'scripts\release-tooling.ps1'),
            "function Test-PublicationEligibility { return [pscustomobject]@{ Eligible = `$true; Failures = @() } }`n",
            [Text.UTF8Encoding]::new($false))
        New-Item -ItemType Directory -Path (Join-Path $testRoot 'tb-malicious-src\scripts') -Force | Out-Null
        [IO.File]::WriteAllText((Join-Path $testRoot 'tb-malicious-src\scripts\release-tooling.ps1'),
            "function Test-PublicationEligibility { return [pscustomobject]@{ Eligible = `$true; Failures = @() } }`n",
            [Text.UTF8Encoding]::new($false))
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -RequireSigning -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
        Assert-True (-not $gate.Eligible) 'hostile candidate tooling must not be able to flip the Stage B verdict'
        Assert-True ($gate.Failures.Count -eq 1) "hostile candidate tooling must not alter the gate: expected exactly the Authenticode failure, got: $($gate.Failures -join '; ')"
        Assert-True (($gate.Failures[0] -match 'Authenticode') -or ($gate.Failures[0] -match 'signtool')) 'the single failure must remain the independent Authenticode re-verification'
    }

    New-TestCase 'missing-release-policy-schema-fails-production' {
        $art = New-SyntheticArtifactDir $testRoot 'tb-no-schema' -SourceSha $goodSha -Version $goodVersion
        Set-SyntheticSignedManifest $art.ManifestPath $art.ArtifactSha256
        Update-TestManifest $art.ManifestPath @{ releasePolicySchemaVersion = $null }
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -RequireSigning -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
        Assert-True (-not $gate.Eligible) 'a manifest without releasePolicySchemaVersion must fail production'
        Assert-True (($gate.Failures -join ';') -match 'releasePolicySchemaVersion') 'failure must cite the schema contract'
    }

    New-TestCase 'stale-policy-schema-fails-production' {
        $art = New-SyntheticArtifactDir $testRoot 'tb-stale-schema' -SourceSha $goodSha -Version $goodVersion
        Set-SyntheticSignedManifest $art.ManifestPath $art.ArtifactSha256
        Update-TestManifest $art.ManifestPath @{ releasePolicySchemaVersion = 2 }
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -RequireSigning -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
        Assert-True (-not $gate.Eligible) 'a manifest with a stale policy schema must fail production'
        Assert-True (($gate.Failures -join ';') -match 'minimum accepted production policy schema') 'failure must cite the minimum schema requirement'
    }

    New-TestCase 'current-policy-schema-accepted' {
        Assert-True ((Get-ReleasePolicySchemaVersion) -eq 3) 'the CURRENT policy schema must be 3'
        Assert-True ((Get-MinimumAcceptedProductionPolicySchema) -eq 3) 'the minimum accepted production policy schema must be 3'
        $art = New-SyntheticArtifactDir $testRoot 'tb-current-schema' -SourceSha $goodSha -Version $goodVersion
        $onDisk = Get-Content -LiteralPath $art.ManifestPath -Raw | ConvertFrom-Json
        Assert-True ([int]$onDisk.releasePolicySchemaVersion -eq 3) 'synthetic manifests must record the current schema'
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
        Assert-True ($gate.Eligible) "a manifest with the current policy schema must pass the non-signing gate: $($gate.Failures -join '; ')"
    }

    New-TestCase 'stage-a-production-sha-mismatch-fails-before-credentials-build' {
        # Static ordering guarantee: the trusted production dispatch contract
        # (inputs.sha == github.sha) is the FIRST step, before checkout,
        # before any signing credentials, before restore/build.
        $yml = [IO.File]::ReadAllText((Join-Path $repoRoot '.github\workflows\prepare-release-candidate.yml'))
        $contract = $yml.IndexOf('Verify trusted production dispatch contract')
        $checkout = $yml.IndexOf('actions/checkout')
        $apiKey = $yml.IndexOf('SM_API_KEY')
        $dotnet = $yml.IndexOf('actions/setup-dotnet')
        Assert-True ($contract -ge 0) 'Stage A must contain the trusted dispatch contract step'
        Assert-True ($contract -lt $checkout) 'the dispatch contract must run before any checkout'
        Assert-True ($contract -lt $apiKey) 'the dispatch contract must run before signing credentials are referenced'
        Assert-True ($contract -lt $dotnet) 'the dispatch contract must run before restore/build tooling'
        Assert-True ($yml -match 'if \(\$env:REQUESTED_SHA -ne \$env:DISPATCH_SHA\)') 'Stage A must fail when the requested SHA differs from the trusted dispatch SHA'
        Assert-True ($yml -match 'no credentials are touched and nothing is built') 'Stage A must document the fail-before-credentials/build contract'
    }

    New-TestCase 'stage-a-production-ref-not-main-fails' {
        $yml = [IO.File]::ReadAllText((Join-Path $repoRoot '.github\workflows\prepare-release-candidate.yml'))
        Assert-True ($yml -match 'refs/heads/main') 'Stage A must require the trusted ref main'
        Assert-True ($yml -match 'WORKFLOW_REF') 'Stage A must inspect the dispatched ref'
        Assert-True ($yml -match 'if \(\$env:WORKFLOW_REF -ne ''refs/heads/main''\)') 'Stage A must fail when dispatched from anything other than main'
        Assert-True ($yml -match 'github.workflow_sha') 'Stage A must bind the trusted policy revision to the workflow file being executed'
    }

    New-TestCase 'stage-b-run-head-sha-vs-manifest-source-commit-mismatch-fails' {
        # Stage B semantics: the run head SHA is the candidate SHA; the
        # manifest must agree. A Stage A run whose head SHA differs from the
        # manifest's recorded source commit must fail closed.
        $art = New-SyntheticArtifactDir $testRoot 'tb-headsha' -SourceSha $goodSha -Version $goodVersion
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha ('d' * 40) -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha ('d' * 40) -ExpectedVersion $goodVersion -EvidencePath $evidencePath -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
        Assert-True (-not $gate.Eligible) 'a run-head-SHA/manifest sourceCommitSha disagreement must fail closed'
        Assert-True (($gate.Failures -join ';') -match 'sourceCommitSha') 'failure must cite the source SHA binding'
    }

    New-TestCase 'publisher-identity-missing-fails-production' {
        $art = New-SyntheticArtifactDir $testRoot 'tb-pub-missing' -SourceSha $goodSha -Version $goodVersion
        Set-SyntheticSignedManifest $art.ManifestPath $art.ArtifactSha256
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        # Deliberately no -ExpectedPublisherSubject: the CURRENT policy must
        # refuse to evaluate production publication without its publisher
        # identity policy.
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -RequireSigning -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName
        Assert-True (-not $gate.Eligible) 'production publication without the current publisher policy must fail'
        Assert-True (($gate.Failures -join ';') -match 'SIGNING_EXPECTED_SUBJECT') 'failure must name the missing publisher policy'
    }

    New-TestCase 'publisher-identity-mismatch-fails-production' {
        $art = New-SyntheticArtifactDir $testRoot 'tb-pub-mismatch' -SourceSha $goodSha -Version $goodVersion
        Set-SyntheticSignedManifest $art.ManifestPath $art.ArtifactSha256
        Update-TestManifest $art.ManifestPath @{ signingCertificateSubject = 'CN=Wrong Publisher, O=Wrong, C=US' }
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -RequireSigning -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
        Assert-True (-not $gate.Eligible) 'a manifest recording a publisher the CURRENT policy does not approve must fail'
        Assert-True (($gate.Failures -join ';') -match 'publisher policy') 'failure must cite the current publisher policy'
    }

    New-TestCase 'manifest-and-file-consistent-but-wrong-against-current-publisher-policy-fails' {
        # The dangerous case: the artifact and its manifest consistently
        # record the WRONG publisher (a valid-looking complete certificate
        # identity that is internally consistent). The CURRENT publisher
        # policy must reject it - "actual cert == manifest cert" is never
        # sufficient; both must equal the CURRENT policy.
        $art = New-SyntheticArtifactDir $testRoot 'tb-pub-consistent-wrong' -SourceSha $goodSha -Version $goodVersion
        Set-SyntheticSignedManifest $art.ManifestPath $art.ArtifactSha256
        Update-TestManifest $art.ManifestPath @{
            signingCertificateSubject    = 'CN=Wrong Publisher, O=Wrong, C=US'
            signingCertificateThumbprint = '3333333333333333333333333333333333333333'
            signingCertificateIssuer     = 'CN=Wrong CA, O=Wrong, C=US'
        }
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -RequireSigning -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
        Assert-True (-not $gate.Eligible) 'an internally consistent wrong-publisher candidate must fail the CURRENT publisher policy'
        Assert-True (($gate.Failures -join ';') -match 'publisher policy') 'failure must cite the current publisher policy, not merely a manifest-internal mismatch'
    }

    New-TestCase 'publisher-policy-required-in-production-preflight' {
        # Stage A layer: Test-ProductionSigningPreflight must block an
        # approved, fully-credentialed provider when the publisher identity
        # policy is missing, and pass when it is configured.
        $p12 = Join-Path $testRoot 'tb-preflight-client.p12'
        [IO.File]::WriteAllBytes($p12, [byte[]]::new(64))
        $envNames = @('SM_HOST', 'SM_API_KEY', 'SM_CLIENT_CERT_FILE', 'SM_CLIENT_CERT_PASSWORD', 'SM_KEYPAIR_ALIAS', 'SIGNING_EXPECTED_SUBJECT')
        $saved = @{}
        foreach ($name in $envNames) { $saved[$name] = [Environment]::GetEnvironmentVariable($name) }
        $previousProvider = [Environment]::GetEnvironmentVariable('SIGNING_PROVIDER')
        [Environment]::SetEnvironmentVariable('SIGNING_PROVIDER', 'digicert-stm')
        try {
            [Environment]::SetEnvironmentVariable('SM_HOST', 'https://example.invalid')
            [Environment]::SetEnvironmentVariable('SM_API_KEY', 'test-api-key')
            [Environment]::SetEnvironmentVariable('SM_CLIENT_CERT_FILE', $p12)
            [Environment]::SetEnvironmentVariable('SM_CLIENT_CERT_PASSWORD', 'test-password')
            [Environment]::SetEnvironmentVariable('SM_KEYPAIR_ALIAS', 'test-keypair')
            [Environment]::SetEnvironmentVariable('SIGNING_EXPECTED_SUBJECT', '')
            $blocked = Test-ProductionSigningPreflight
            Assert-True (-not $blocked.Approved -or $blocked.BlockedReason -match 'SIGNING_EXPECTED_SUBJECT') 'preflight must block an approved provider without the publisher policy'
            [Environment]::SetEnvironmentVariable('SIGNING_EXPECTED_SUBJECT', 'CN=TabDock Test Publisher, O=TabDock Test, C=US')
            $ok = Test-ProductionSigningPreflight
            Assert-True ($ok.Approved -and $ok.Configured -and [string]::IsNullOrWhiteSpace($ok.BlockedReason)) "preflight must pass with the publisher policy configured: $($ok.BlockedReason)"
        }
        finally {
            foreach ($name in $envNames) {
                if ($null -eq $saved[$name]) { Remove-Item "Env:\$name" -ErrorAction SilentlyContinue }
                else { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
            }
            if ($null -eq $previousProvider) { Remove-Item Env:\SIGNING_PROVIDER -ErrorAction SilentlyContinue }
            else { [Environment]::SetEnvironmentVariable('SIGNING_PROVIDER', $previousProvider) }
        }
    }

    New-TestCase 'production-stage-a-receives-no-pfx-secrets' {
        # Least privilege: the production Stage A job must not receive the
        # legacy exportable-PFX secrets at all (they have no legitimate role
        # in the HSM path; RC/local paths keep them elsewhere).
        $yml = [IO.File]::ReadAllText((Join-Path $repoRoot '.github\workflows\prepare-release-candidate.yml'))
        foreach ($secret in @('SIGNCERT_BASE64', 'SIGNCERT_PASSWORD', 'SIGNCERT_TIMESTAMP')) {
            Assert-True ($yml -notmatch [regex]::Escape($secret)) "prepare-release-candidate.yml must not expose '$secret'"
        }
    }

    New-TestCase 'digicert-action-pinned-to-full-immutable-sha' {
        # The signing control plane receives highly sensitive service
        # authentication material; a mutable major tag is never trusted.
        $yml = [IO.File]::ReadAllText((Join-Path $repoRoot '.github\workflows\prepare-release-candidate.yml'))
        Assert-True ($yml -match 'digicert/code-signing-software-trust-action@fae23a455ba4bde62b64fd7cb2f81ade788f5a95') 'the DigiCert action must be pinned to the full 40-character immutable SHA'
        Assert-True ($yml -notmatch 'digicert/code-signing-software-trust-action@v\d') 'a mutable major tag must never be used for the DigiCert action'
        Assert-True ($yml -match 'v1\.2\.1') 'the pinned SHA must be documented with its human-readable release version'
    }

    New-TestCase 'stage-b-trusted-policy-path-points-to-policy-not-candidate-source' {
        $yml = [IO.File]::ReadAllText((Join-Path $repoRoot '.github\workflows\publish-release.yml'))
        $expected = 'policy/scripts/release-tooling.ps1'
        $count = ([regex]::Matches($yml, [regex]::Escape($expected))).Count
        Assert-True ($count -ge 2) "Stage B must dot-source the trusted module from policy/ in every job (found $count)"
        Assert-True ($yml -notmatch [regex]::Escape('candidate-source/scripts')) 'Stage B must never load scripts from candidate-source/'
        Assert-True ($yml -notmatch [regex]::Escape("(Join-Path `$env:GITHUB_WORKSPACE 'scripts/release-tooling.ps1')")) 'the old candidate-controlled dot-source pattern must be gone'
    }

    New-TestCase 'hosted-build-workflow-invokes-release-tooling-tests' {
        # The 96+ release-control cases must be an exact-SHA hosted-CI gate.
        $yml = [IO.File]::ReadAllText((Join-Path $repoRoot '.github\workflows\build.yml'))
        Assert-True ($yml -match 'release-tooling-tests\.ps1') 'build.yml must invoke the release-tooling regression suite'
    }

    New-TestCase 'stage-b-verify-job-has-no-contents-write' {
        # Least privilege: the verify job's permissions block must be
        # contents: read only (plus the documented actions: write deviation
        # for the same-run handoff upload); the release-mutation permission
        # exists only in the publish job.
        $yml = [IO.File]::ReadAllText((Join-Path $repoRoot '.github\workflows\publish-release.yml'))
        $verifyIdx = $yml.IndexOf('  verify:')
        $publishIdx = $yml.IndexOf('  publish:')
        Assert-True ($verifyIdx -ge 0 -and $publishIdx -gt $verifyIdx) 'Stage B must contain verify and publish jobs in that order'
        $verifySection = $yml.Substring($verifyIdx, $publishIdx - $verifyIdx)
        $permStart = $verifySection.IndexOf('permissions:')
        $permEnd = $verifySection.IndexOf('outputs:')
        Assert-True ($permStart -ge 0 -and $permEnd -gt $permStart) 'the verify job must declare permissions followed by outputs'
        $verifyPerms = $verifySection.Substring($permStart, $permEnd - $permStart)
        Assert-True ($verifyPerms -match 'contents: read') 'the verify job must declare contents: read'
        Assert-True ($verifyPerms -notmatch 'contents: write') 'the verify job permissions must NOT include contents: write'
        Assert-True ($verifyPerms -match 'actions: write') 'the verify job must hold actions: write for the same-run handoff upload (documented deviation; no release/tag capability)'
        $publishSection = $yml.Substring($publishIdx)
        Assert-True ($publishSection -match 'contents: write') 'the publish job must declare contents: write'
        Assert-True ($publishSection -match 'needs: verify') 'the publish job must depend on the verify job'
    }

    New-TestCase 'stage-b-publish-job-performs-no-build-sign-or-candidate-execution' {
        # The contents:write-capable job performs only the final hash identity
        # check, the release mutation, and asset verification.
        $yml = [IO.File]::ReadAllText((Join-Path $repoRoot '.github\workflows\publish-release.yml'))
        $publishIdx = $yml.IndexOf('  publish:')
        Assert-True ($publishIdx -ge 0) 'Stage B must contain the publish job'
        $publishSection = $yml.Substring($publishIdx)
        foreach ($forbidden in @('dotnet build', 'dotnet publish', 'sign-release', 'release-qualify', 'smctl',
                '--selftest', '--version', '& $exe', 'SIGNING_PROVIDER', 'SIGNCERT_', 'SM_API_KEY',
                'SM_CLIENT_CERT', 'SM_KEYPAIR_ALIAS', 'digicert', 'candidate-source')) {
            Assert-True ($publishSection -notmatch [regex]::Escape($forbidden)) "the Stage B publish job must not contain '$forbidden'"
        }
        Assert-True ($publishSection -match 'gh release create') 'the publish job must create the release'
        Assert-True ($publishSection -match 'final hash identity') 'the publish job must perform the final hash identity check'
    }

    New-TestCase 'stage-b-policy-checkout-is-the-running-workflow-revision' {
        # The trusted policy checkout ref must be github.sha (== the revision
        # of publish-release.yml being executed) and the workflow must fail
        # closed when the dispatched ref is not main or when
        # github.workflow_sha disagrees with github.sha.
        $yml = [IO.File]::ReadAllText((Join-Path $repoRoot '.github\workflows\publish-release.yml'))
        Assert-True ($yml -match [regex]::Escape("ref: `${{ github.sha }}")) 'the trusted policy checkout must use github.sha'
        Assert-True ($yml -match 'path: policy') 'the trusted policy checkout must land in policy/'
        Assert-True ($yml -match 'github.workflow_sha') 'Stage B must bind the policy revision to the workflow file being executed'
        Assert-True ($yml -match [regex]::Escape("`$env:WORKFLOW_SHA -ne `$env:DISPATCH_SHA")) 'Stage B must fail when the workflow-file revision differs from the dispatch commit'
        Assert-True ($yml -match 'refs/heads/main') 'Stage B must require dispatch from main'
    }

    New-TestCase 'timestamp-missing-fails-production' {
        # RFC3161 timestamp policy: an unverified/absent timestamp state and a
        # manifest without timestamper identity both fail closed.
        $art = New-SyntheticArtifactDir $testRoot 'tb-ts-missing' -SourceSha $goodSha -Version $goodVersion
        Set-SyntheticSignedManifest $art.ManifestPath $art.ArtifactSha256
        Update-TestManifest $art.ManifestPath @{ timestampStatus = 'NOT_PERFORMED' }
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -RequireSigning -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
        Assert-True (-not $gate.Eligible) 'a manifest with timestampStatus != VERIFIED must fail production'
        Assert-True (($gate.Failures -join ';') -match 'timestampStatus') 'failure must cite the timestamp status'

        $art2 = New-SyntheticArtifactDir $testRoot 'tb-ts-identity' -SourceSha $goodSha -Version $goodVersion
        Set-SyntheticSignedManifest $art2.ManifestPath $art2.ArtifactSha256
        Update-TestManifest $art2.ManifestPath @{ timestampCertificateSubject = $null; timestampCertificateThumbprint = $null }
        $evidencePath2 = Join-Path $art2.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath2 (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art2.ArtifactSha256)
        $gate2 = Test-PublicationEligibility -ArtifactDir $art2.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath2 -RequireSigning -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
        Assert-True (-not $gate2.Eligible) 'a manifest without timestamper identity must fail production'
        Assert-True (($gate2.Failures -join ';') -match 'timestampCertificate') 'failure must cite the missing timestamper identity'
    }

    New-TestCase 'timestamp-warning-non-valid-state-fails' {
        # A warning/non-valid timestamp state is never acceptable: signtool
        # /tw turns an untimestamped file into a non-zero (warning) result,
        # and the manifest-level policy rejects anything but VERIFIED.
        $art = New-SyntheticArtifactDir $testRoot 'tb-ts-warned' -SourceSha $goodSha -Version $goodVersion
        Set-SyntheticSignedManifest $art.ManifestPath $art.ArtifactSha256
        Update-TestManifest $art.ManifestPath @{ timestampStatus = 'WARNED' }
        $evidencePath = Join-Path $art.Dir 'release-external-evidence.json'
        Save-TestEvidence $evidencePath (New-TestEvidence -SourceSha $goodSha -ArtifactSha $art.ArtifactSha256)
        $gate = Test-PublicationEligibility -ArtifactDir $art.Dir -ExpectedSourceSha $goodSha -ExpectedVersion $goodVersion -EvidencePath $evidencePath -RequireSigning -ExpectedCandidateRunId $goodRunId -ExpectedCandidateArtifactName $goodArtifactName -ExpectedPublisherSubject $goodPublisherSubject
        Assert-True (-not $gate.Eligible) 'timestampStatus=WARNED must fail production (only VERIFIED is acceptable)'
        # The verification helper itself must fail closed on an untimestamped
        # file (unsigned test artifact -> no timestamp possible).
        $exe = Join-Path $testRoot 'tb-ts-unsigned.exe'
        New-DummyArtifact $exe
        Assert-True (-not (Test-AuthenticodeTimestamp $exe)) 'an untimestamped artifact must fail timestamp verification (fail closed)'
    }

    Write-Host ''
    Write-Host '==> Two-stage workflow structural guarantees (static workflow review)' -ForegroundColor Cyan

    New-TestCase 'publish-workflow-cannot-build-sign-or-qualify' {
        $yml = [IO.File]::ReadAllText((Join-Path $repoRoot '.github\workflows\publish-release.yml'))
        foreach ($forbidden in @('dotnet publish', 'sign-release.ps1', 'release-qualify.ps1',
                'RELEASE_SIGNING_REQUIRED', 'RELEASE_PRODUCTION_GATE', 'create-release',
                'SIGNING_PROVIDER', 'SIGNCERT_BASE64', 'SIGNCERT_PASSWORD', 'SM_HOST', 'SM_API_KEY',
                'SM_CLIENT_CERT', 'SM_KEYPAIR_ALIAS', 'digicert', 'smctl')) {
            Assert-True ($yml -notmatch [regex]::Escape($forbidden)) "publish-release.yml must not contain '$forbidden' (Stage B never builds, signs, qualifies, or contacts a signing provider)"
        }
        Assert-True ($yml -match 'actions/download-artifact@v7') 'Stage B must download the Stage A artifact'
        Assert-True ($yml -match 'run-id') 'Stage B must bind to the Stage A run id'
        Assert-True ($yml -match 'actions: read') 'Stage B needs actions: read for the cross-run artifact download'
        Assert-True ($yml -match 'gh release create') 'Stage B must be the only workflow that creates the release'
        Assert-True ($yml -match 'Get-ReleaseTagFromVersion') 'Stage B must derive the tag from the semantic version'
        # The ONLY upload in Stage B is the verified same-run handoff (the
        # publish job re-downloads the Stage A bytes itself); the candidate
        # artifact is downloaded, never uploaded.
        Assert-True ($yml -match 'upload-artifact') 'Stage B verify job must upload the verified same-run handoff'
        Assert-True ($yml -match 'tabdock-verified-\${{ github.run_id }}') 'the Stage B handoff artifact uses the verified-handoff naming scheme'
        $uploadIdx = $yml.IndexOf('actions/upload-artifact')
        Assert-True ($uploadIdx -ge 0) 'Stage B must contain an upload-artifact step (verified handoff)'
        $nextStep = $yml.IndexOf('      - name:', $uploadIdx + 10)
        $uploadStep = if ($nextStep -ge 0) { $yml.Substring($uploadIdx, $nextStep - $uploadIdx) } else { $yml.Substring($uploadIdx) }
        Assert-True ($uploadStep -match 'path: verified-handoff') 'the Stage B upload must be the verified handoff directory'
        Assert-True ($uploadStep -notmatch 'candidate-artifact') 'Stage B must never upload the candidate artifact'
    }

    New-TestCase 'prepare-candidate-workflow-forces-approved-provider-and-never-publishes' {
        $yml = [IO.File]::ReadAllText((Join-Path $repoRoot '.github\workflows\prepare-release-candidate.yml'))
        Assert-True ($yml -match "RELEASE_SIGNING_REQUIRED:\s*'true'") 'Stage A must force signing'
        Assert-True ($yml -match "RELEASE_PRODUCTION_GATE:\s*'true'") 'Stage A must force the production gate'
        Assert-True ($yml -match 'BLOCKED_EXTERNAL') 'Stage A must block explicitly when the production signer is unavailable'
        Assert-True ($yml -match 'SIGNING_PROVIDER') 'Stage A must select the signing provider explicitly'
        Assert-True ($yml -match 'Test-ApprovedProductionSigningProvider') 'Stage A must require an APPROVED production provider (reject local-pfx/mock/not-configured)'
        Assert-True ($yml -match 'Get-SigningProviderCredentialRequirements') 'Stage A must validate the selected provider credentials'
        Assert-True ($yml -match 'digicert/code-signing-software-trust-action@fae23a455ba4bde62b64fd7cb2f81ade788f5a95') 'Stage A must pin the official DigiCert tooling action to its full immutable commit SHA (v1.2.1)'
        Assert-True ($yml -notmatch 'digicert/code-signing-software-trust-action@v\d') 'Stage A must never float a mutable major tag for the DigiCert signing control plane'
        Assert-True ($yml -notmatch 'smctl sign') 'Stage A must never invoke smctl directly in the workflow; signing happens exactly once inside sign-release.ps1'
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

        New-TestCase 'release-qualify-rejects-mock-provider-always' {
            # The test-only mock provider must never be usable for release
            # qualification, with or without a production gate.
            $headSha = (git rev-parse HEAD).Trim()
            $env:SIGNING_PROVIDER = 'mock-test'
            try {
                $output = & (Join-Path $scratch 'scripts\release-qualify.ps1') -Sha $headSha -SkipOpenSpec 6>&1 2>&1 | Out-String
                Assert-True ($LASTEXITCODE -ne 0) "mock provider must be refused by release-qualify`n$output"
                Assert-True ($output -match 'mock signing provider') "failure must cite the mock provider`n$output"
            }
            finally {
                Remove-Item Env:\SIGNING_PROVIDER -ErrorAction SilentlyContinue
            }
        }

        New-TestCase 'release-qualify-production-gate-rejects-local-pfx-before-build' {
            $headSha = (git rev-parse HEAD).Trim()
            $env:SIGNING_PROVIDER = 'local-pfx'
            $env:RELEASE_PRODUCTION_GATE = 'true'
            try {
                $output = & (Join-Path $scratch 'scripts\release-qualify.ps1') -Sha $headSha -SkipOpenSpec 6>&1 2>&1 | Out-String
                Assert-True ($LASTEXITCODE -ne 0) "local-pfx under the production gate must fail`n$output"
                Assert-True ($output -match 'not an approved production signer') "failure must cite the approval policy`n$output"
            }
            finally {
                Remove-Item Env:\SIGNING_PROVIDER, Env:\RELEASE_PRODUCTION_GATE -ErrorAction SilentlyContinue
            }
        }

        New-TestCase 'release-qualify-production-gate-blocks-incomplete-cloud-provider-before-build' {
            # digicert-stm selected but its credentials incomplete: the run
            # must fail BLOCKED_EXTERNAL before any build, naming ONLY the
            # missing variable names.
            $headSha = (git rev-parse HEAD).Trim()
            $env:SIGNING_PROVIDER = 'digicert-stm'
            $env:RELEASE_PRODUCTION_GATE = 'true'
            try {
                $output = & (Join-Path $scratch 'scripts\release-qualify.ps1') -Sha $headSha -SkipOpenSpec 6>&1 2>&1 | Out-String
                Assert-True ($LASTEXITCODE -ne 0) "incomplete digicert-stm under the production gate must fail`n$output"
                Assert-True ($output -match 'BLOCKED_EXTERNAL') "failure must be BLOCKED_EXTERNAL`n$output"
                Assert-True ($output -match 'SM_API_KEY') "failure must name the missing variables`n$output"
            }
            finally {
                Remove-Item Env:\SIGNING_PROVIDER, Env:\RELEASE_PRODUCTION_GATE -ErrorAction SilentlyContinue
            }
        }

        New-TestCase 'release-qualify-rc-local-pfx-preflight-passes-then-proceeds' {
            # RC (no production gate) with signing required and complete
            # local-pfx credentials: the provider preflight must PASS and the
            # run must proceed past it (the minimal scratch project then
            # fails at publish - expected - which proves the preflight did
            # not block the RC path).
            $headSha = (git rev-parse HEAD).Trim()
            $env:SIGNING_PROVIDER = 'local-pfx'
            $env:SIGNCERT_BASE64 = 'c2hvdWxkLW5ldmVyLWJlLXVzZWQ='
            $env:SIGNCERT_PASSWORD = 'pw'
            $env:RELEASE_SIGNING_REQUIRED = 'true'
            try {
                $output = & (Join-Path $scratch 'scripts\release-qualify.ps1') -Sha $headSha -SkipOpenSpec 6>&1 2>&1 | Out-String
                Assert-True ($output -match 'signing provider preflight: local-pfx') "RC local-pfx preflight must pass`n$output"
                Assert-True ($output -notmatch 'BLOCKED_EXTERNAL') "RC local-pfx must not be blocked by the preflight`n$output"
            }
            finally {
                Remove-Item Env:\SIGNING_PROVIDER, Env:\SIGNCERT_BASE64, Env:\SIGNCERT_PASSWORD, Env:\RELEASE_SIGNING_REQUIRED -ErrorAction SilentlyContinue
            }
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
