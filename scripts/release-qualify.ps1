<#
.SYNOPSIS
    Exact-SHA release qualification and artifact provenance for TabDock.

.DESCRIPTION
    Produces the single-file Release executable, qualifies THAT exact binary
    (version identity, embedded source commit, hermetic self-tests), computes
    its SHA-256, applies optional Authenticode signing, and writes
    release-manifest.json plus SHA256SUMS.txt beside the artifact.

    The chain is intentionally immutable:
        intended SHA -> actual HEAD -> audited restore -> publish ->
        execute + qualify the published executable -> optional signing ->
        signature verification -> FINAL SHA-256 -> manifest -> checksums

    No second compilation happens after the artifact is qualified: the
    executable that passes qualification is the executable that is hashed,
    manifested, and (when a release is created) uploaded.

    Hash semantics (enforced by scripts/release-tooling.ps1):
        unsignedQualifiedSha256 = hash of the executable that passed pre-sign
                                  qualification (always retained)
        finalSignedSha256       = hash after Authenticode signing + verification
                                  (only when signing changed the bytes)
        artifactSha256          = hash of the FINAL DISTRIBUTED executable
        SHA256SUMS.txt          = hash of the FINAL DISTRIBUTED executable
    The manifest and SHA256SUMS.txt are written only AFTER signing, from the
    hash of the artifact as it exists at finalization time, so a signed
    release never ships checksums that describe the unsigned executable.

.PARAMETER Sha
    The exact source commit the release must be built from. When empty, HEAD
    is used and reported. A non-empty mismatch fails the qualification.

.PARAMETER Version
    EXPECTED semantic version only: TabDock.csproj <Version> is the single
    authoritative version and the manifest always records it. When a version
    is supplied here it must equal the project version; any disagreement
    fails the qualification.

.PARAMETER OutDir
    Directory for TabDock.exe, SHA256SUMS.txt, and release-manifest.json.
    Defaults to <repo>/.artifacts/release.

.PARAMETER Ci
    Enable CI policy: audited NuGet restore, OpenSpec validation, and no
    worktree-dirty failure for a fresh checkout.

.PARAMETER Sign
    Attempt Authenticode signing when a signing provider is configured
    (SIGNING_PROVIDER, see scripts/sign-release.ps1). Without a provider the
    manifest records NOT_CONFIGURED; with RELEASE_SIGNING_REQUIRED=true the
    qualification fails instead.

.PARAMETER SkipOpenSpec
    Skip OpenSpec validation (local convenience when the pinned CLI is not
    installed and no global openspec exists).

.DESCRIPTION
    Production policy: when RELEASE_PRODUCTION_GATE=true (the
    prepare-release-candidate workflow sets it for Stage A production
    candidates), Authenticode signing becomes mandatory exactly as if
    RELEASE_SIGNING_REQUIRED=true, the signing provider must be an APPROVED
    production provider (currently digicert-stm, key protection class
    CLOUD_HSM - a non-exportable private key held by the signing service),
    the provider's credentials must be complete (BLOCKED_EXTERNAL otherwise,
    BEFORE any build work), test-only mock signing is refused, the manifest
    records releaseMode=PRODUCTION, and the CURRENT publisher identity policy
    (SIGNING_EXPECTED_SUBJECT) must be configured and must equal the signed
    certificate subject (the manifest also records the current
    releasePolicySchemaVersion, which the Stage B gate requires). Local-PFX
    signing (exportable key) is NEVER approved for production candidates.
    External human gates (final smoke, mixed-DPI, Windows compatibility) are
    NEVER verified by this script; productionReleaseEligibility is therefore
    BLOCKED_EXTERNAL here and the publish-release workflow (Stage B)
    independently validates the external evidence file before creating the
    release.
#>
[CmdletBinding()]
param(
    [string]$Sha = '',
    [string]$Version = '',
    [string]$OutDir = '',
    [switch]$Ci,
    [switch]$Sign,
    [switch]$SkipOpenSpec
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'release-tooling.ps1')

$RepoRoot      = Split-Path -Parent $PSScriptRoot
$MainProject   = Join-Path $RepoRoot 'TabDock.csproj'
$OpenSpecLocal = Join-Path $RepoRoot 'tools\openspec\node_modules\.bin\openspec.cmd'

if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $OutDir = Join-Path $RepoRoot '.artifacts\release'
}
$OutDir = [IO.Path]::GetFullPath($OutDir)
$ArtifactExe = Join-Path $OutDir 'TabDock.exe'

function Invoke-Step {
    param([string]$Name, [scriptblock]$Body)
    Write-Host ''
    Write-Host "==> $Name" -ForegroundColor Cyan
    & $Body
    if ($LASTEXITCODE -ne 0) {
        throw "FAILED: $Name (exit code $LASTEXITCODE)"
    }
}

function ConvertTo-ProcessArgumentLine {
    param([string[]]$Arguments)
    return (($Arguments | ForEach-Object { '"' + $_.Replace('"', '\"') + '"' }) -join ' ')
}

function Invoke-Executable {
    param([string]$Name, [string]$Path, [string[]]$Arguments)
    Write-Host ''
    Write-Host "==> $Name" -ForegroundColor Cyan
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Executable not found: $Path"
    }
    $argumentLine = ConvertTo-ProcessArgumentLine $Arguments
    $process = Start-Process -FilePath $Path -ArgumentList $argumentLine -NoNewWindow -Wait -PassThru
    if ($process.ExitCode -ne 0) {
        throw "FAILED: $Name (exit code $($process.ExitCode))"
    }
}

function Invoke-Captured {
    param([string]$Path, [string[]]$Arguments, [int]$TimeoutSeconds = 300)
    $argumentLine = ConvertTo-ProcessArgumentLine $Arguments
    $psi = [Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $Path
    $psi.Arguments = $argumentLine
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $psi
    if (-not $process.Start()) { throw "Could not start: $Path" }
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        $process.Kill()
        throw "$Path did not exit within $TimeoutSeconds seconds"
    }
    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    return [pscustomobject]@{ ExitCode = $process.ExitCode; Stdout = $stdout; Stderr = $stderr }
}

$verdict = 'FAIL'
$failureReason = ''
$productionGate = [string]::Equals($env:RELEASE_PRODUCTION_GATE, 'true', [StringComparison]::OrdinalIgnoreCase)
$signingRequired = [string]::Equals($env:RELEASE_SIGNING_REQUIRED, 'true', [StringComparison]::OrdinalIgnoreCase) -or $productionGate
$manifest = [ordered]@{
    product             = 'TabDock'
    semanticVersion     = 'unavailable'
    sourceCommitSha     = 'unavailable'
    artifactFileName    = 'TabDock.exe'
    artifactSha256      = 'unavailable'
    unsignedQualifiedSha256 = 'unavailable'
    finalSignedSha256   = $null
    targetRuntimeIdentifier = 'win-x64'
    configuration       = 'Release'
    buildIdentity       = [ordered]@{ semanticVersion = 'unavailable'; informationalVersion = 'unavailable'; selfReportedSha256 = 'unavailable' }
    signingStatus       = 'NOT_CONFIGURED'
    signatureVerification = 'NOT_PERFORMED'
    signingProvider     = 'not-configured'
    signingKeyProtection = 'NOT_CONFIGURED'
    timestampStatus     = 'NOT_PERFORMED'
    signingCertificateSubject = $null
    signingCertificateThumbprint = $null
    signingCertificateIssuer = $null
    signingCertificateSerialNumber = $null
    signingCertificateValidFrom = $null
    signingCertificateValidTo = $null
    signingCertificateEku = $null
    timestampCertificateSubject = $null
    timestampCertificateThumbprint = $null
    signingMock         = $null
    releaseMode         = if ($productionGate) { 'PRODUCTION' } else { 'QUALIFICATION_ONLY' }
    # The release-policy schema generation of the CURRENT trusted policy under
    # which this candidate is being produced. Stage B (the CURRENT policy)
    # rejects candidates whose schema is absent or below the minimum, so an
    # old candidate is never evaluated under its own historical policy.
    releasePolicySchemaVersion = Get-ReleasePolicySchemaVersion
    # Qualification-time truth: external human gates are never verified by
    # this script, so production eligibility is BLOCKED_EXTERNAL here. The
    # release workflow's publish job validates the external evidence file and
    # records the ELIGIBLE/FAIL verdict in publication-verification.json.
    productionReleaseEligibility = 'BLOCKED_EXTERNAL'
    qualificationStatus = $verdict
    qualificationTimestamp = [DateTimeOffset]::UtcNow.ToString('O')
    workflowRunId       = $env:GITHUB_RUN_ID
    externalGates       = [ordered]@{
        finalWindowsHumanSmoke   = 'BLOCKED_EXTERNAL'
        physicalMixedDpi         = 'BLOCKED_EXTERNAL'
        browserCoverage          = 'SKIP_NOT_APPLICABLE'
        destructiveLogoffTesting = 'SKIP_NOT_APPLICABLE'
        signingCredentials       = 'NOT_CONFIGURED'
    }
}

Push-Location $RepoRoot
try {
    try {
    # --- Exact-SHA and worktree verification --------------------------------
    $intendedSha = $Sha
    $actualSha = (git rev-parse HEAD).Trim()
    Write-Host "intended source SHA: $($(if ([string]::IsNullOrWhiteSpace($intendedSha)) { '(HEAD)' } else { $intendedSha }))" -ForegroundColor Yellow
    Write-Host "actual HEAD SHA:     $actualSha" -ForegroundColor Yellow
    if ([string]::IsNullOrWhiteSpace($intendedSha)) {
        $intendedSha = $actualSha
    }
    if ($intendedSha -ne $actualSha) {
        throw "Exact-SHA mismatch: requested $intendedSha but HEAD is $actualSha"
    }
    $manifest.sourceCommitSha = $actualSha

    $dirty = @(git status --porcelain)
    if ($dirty.Count -gt 0) {
        if ($Ci) {
            throw "Dirty working tree during CI qualification is unexpected (checkout should be exact): $($dirty -join '; ')"
        }
        throw "Dirty working tree: a release candidate must be qualified from a clean exact commit. Commit or stash the $($dirty.Count) changed path(s) first."
    }

    # --- Version authority ---------------------------------------------------
    # TabDock.csproj <Version> is the single authoritative semantic version.
    # The workflow -Version input is only an EXPECTED value and must agree
    # with the project version; the manifest records the project version, so
    # version=9.9.9 can never be recorded while the project still declares
    # 1.0.0. Fails fast, before any restore/build work.
    $projectVersion = Get-ProjectSemanticVersion $MainProject
    if (-not [string]::IsNullOrWhiteSpace($Version) -and $Version -ne $projectVersion) {
        throw "Version authority mismatch: workflow expected version '$Version' != project <Version> '$projectVersion' (TabDock.csproj is authoritative)."
    }
    $manifest.semanticVersion = $projectVersion
    Write-Host "authoritative project version: $projectVersion (from $MainProject)" -ForegroundColor Green
    if (-not [string]::IsNullOrWhiteSpace($Version)) {
        Write-Host "workflow expected version:     $Version (agrees)" -ForegroundColor Green
    }

    # --- Signing provider preflight (fails BEFORE any build work) ------------
    # The signer is selected by SIGNING_PROVIDER and is completely abstract
    # to this script. Production candidates REQUIRE an approved
    # non-exportable-key (HSM/cloud) provider with complete credentials;
    # anything else - local-PFX (exportable key), mock, not-configured,
    # unknown providers - fails here with BLOCKED_EXTERNAL and nothing is
    # built. RC runs may stay NOT_CONFIGURED or use local-pfx. Only variable
    # NAMES are ever reported, never values.
    $invokeSigning = $Sign -or $signingRequired -or -not [string]::IsNullOrWhiteSpace($env:SIGNING_PROVIDER)
    if ($invokeSigning) {
        $signingProvider = Get-SigningProvider
        if ($signingProvider -eq 'mock-test') {
            throw 'release-qualify: the test-only mock signing provider (SIGNING_PROVIDER=mock-test) is never valid for release qualification; use the deterministic test suite instead.'
        }
        if ($signingRequired) {
            if ($productionGate) {
                $preflight = Test-ProductionSigningPreflight
                if (-not $preflight.Approved -or -not $preflight.Configured) {
                    throw "BLOCKED_EXTERNAL: production signing preflight failed - $($preflight.BlockedReason). No candidate is built; configure the approved production signer and re-dispatch."
                }
                Write-Host "signing provider preflight: $($preflight.Provider) (key protection $($preflight.KeyProtection)) - approved production signer, configuration complete" -ForegroundColor Green
            }
            else {
                $cfg = Test-SigningProviderConfiguration $signingProvider
                if (-not $cfg.Configured) {
                    throw "BLOCKED_EXTERNAL: signing is required but provider '$signingProvider' is missing required configuration: $($cfg.Missing -join ', '). No candidate is built."
                }
                Write-Host "signing provider preflight: $signingProvider (key protection $($cfg.KeyProtection)) - configuration complete" -ForegroundColor Green
            }
        }
        else {
            Write-Host "signing provider: $signingProvider (key protection $(Get-SigningProviderKeyProtection $signingProvider))" -ForegroundColor Yellow
        }
    }

    # --- Audited restore ------------------------------------------------------
    if ($Ci) {
        Invoke-Step 'Restore with NuGet audit' {
            dotnet restore $MainProject -p:NuGetAudit=true -p:NuGetAuditMode=all '-warnaserror:NU1900;NU1901;NU1902;NU1903;NU1904' --nologo
        }
        $noRestore = @('--no-restore')
    }
    else {
        $noRestore = @()
    }

    # --- Release publish (single-file, self-contained, win-x64) --------------
    Invoke-Step 'Publish single-file executable (Release, win-x64)' {
        dotnet publish $MainProject -c Release -r win-x64 --self-contained true @noRestore -o $OutDir `
            -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:IncludeNativeLibrariesForSelfExtract=true
    }
    if (-not (Test-Path -LiteralPath $ArtifactExe -PathType Leaf)) {
        throw "Publish did not produce $ArtifactExe"
    }

    # --- Qualify THE EXACT ARTIFACT ------------------------------------------
    $versionRun = Invoke-Captured $ArtifactExe @('--version')
    if ($versionRun.ExitCode -ne 0) {
        throw "Published --version failed: $($versionRun.Stderr)"
    }
    $selfSha = [regex]::Match($versionRun.Stdout, '(?im)^sha256:\s*([0-9A-Fa-f]{64})').Groups[1].Value
    $commitLine = [regex]::Match($versionRun.Stdout, '(?im)^commit:\s*([0-9a-f]{40})').Groups[1].Value
    $binarySemanticVersion = [regex]::Match($versionRun.Stdout, '(?im)^TabDock\s+(\S+)').Groups[1].Value
    $infoVersion = [regex]::Match($versionRun.Stdout, '(?im)^informationalVersion:\s*(\S+)').Groups[1].Value
    $manifest.buildIdentity.semanticVersion = $binarySemanticVersion
    $manifest.buildIdentity.informationalVersion = $infoVersion
    $manifest.buildIdentity.selfReportedSha256 = $selfSha
    if ($commitLine -ne $actualSha) {
        throw "Published executable reports commit $commitLine but the candidate SHA is $actualSha"
    }
    # Binary -> project version authority: the published executable must
    # report the same semantic version the project declares. The informational
    # version must carry that same semantic version plus source identity.
    if ($binarySemanticVersion -ne $projectVersion) {
        throw "Published executable reports semantic version '$binarySemanticVersion' but the authoritative project version is '$projectVersion'"
    }
    if (-not [string]::IsNullOrWhiteSpace($infoVersion)) {
        $infoSemantic = Get-SemanticVersionPart $infoVersion
        if ($infoSemantic -ne $projectVersion) {
            throw "Published executable informational version '$infoVersion' does not carry the authoritative semantic version '$projectVersion'"
        }
    }
    else {
        throw 'Published executable did not report an informationalVersion'
    }
    if ([string]::IsNullOrWhiteSpace($selfSha)) {
        throw 'Published executable did not report its own SHA-256'
    }
    Write-Host "published exe source identity: $commitLine (matches HEAD)" -ForegroundColor Green
    Write-Host "published exe semantic version: $binarySemanticVersion (matches project <Version>)" -ForegroundColor Green
    Write-Host "published exe informational version: $infoVersion" -ForegroundColor Green
    Write-Host "published exe self-reported sha256: $selfSha" -ForegroundColor Green

    Invoke-Executable 'Published geometry self-test (exact artifact)' $ArtifactExe @('--selftest-geometry')
    Invoke-Executable 'Published diagnostics self-test (exact artifact)' $ArtifactExe @('--selftest-diagnostics')

    if ($Ci) {
        $env:OPENSPEC_NO_UPDATE_CHECK = '1'
        $env:OPENSPEC_TELEMETRY = '0'
        if (-not (Test-Path -LiteralPath $OpenSpecLocal -PathType Leaf)) {
            Invoke-Step 'Install repository-owned OpenSpec tooling' {
                Push-Location (Join-Path $RepoRoot 'tools\openspec')
                try { npm ci --ignore-scripts } finally { Pop-Location }
            }
        }
        Invoke-Step 'OpenSpec validation' {
            & $OpenSpecLocal validate --all --no-interactive
        }
    }
    elseif (-not $SkipOpenSpec) {
        $openSpec = if (Test-Path -LiteralPath $OpenSpecLocal -PathType Leaf) { $OpenSpecLocal }
                   else { (Get-Command openspec -ErrorAction SilentlyContinue)?.Source }
        if ($null -ne $openSpec) {
            Invoke-Step 'OpenSpec validation' {
                & $openSpec validate --all --no-interactive
            }
        }
        else {
            Write-Host 'OpenSpec CLI not found; skipping local spec validation (use -SkipOpenSpec to silence).' -ForegroundColor Yellow
        }
    }

    # --- SHA-256 of the exact artifact BEFORE signing ------------------------
    # This is the unsigned provenance hash: the bytes that passed pre-sign
    # qualification. It is retained in unsignedQualifiedSha256 even when
    # Authenticode signing later changes the bytes.
    $unsignedSha = (Get-FileHash -Algorithm SHA256 -LiteralPath $ArtifactExe).Hash.ToUpperInvariant()
    if (-not [string]::Equals($unsignedSha, $selfSha, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Artifact SHA-256 mismatch: Get-FileHash=$unsignedSha but executable self-report=$selfSha"
    }
    $manifest.unsignedQualifiedSha256 = $unsignedSha.ToLowerInvariant()
    Write-Host "SHA-256($($ArtifactExe)) pre-sign = $unsignedSha (matches self-report)" -ForegroundColor Green

    # --- Authenticode signing (provider-abstracted) --------------------------
    # Production candidates (RELEASE_PRODUCTION_GATE=true, set by the
    # prepare-release-candidate workflow) make signing mandatory: a production
    # release is never silently defaulted to unsigned. The signer backend is
    # selected by SIGNING_PROVIDER inside sign-release.ps1; this script only
    # consumes the structured contract and enforces production policy
    # (approved provider, approved non-exportable key-protection class,
    # verified RFC3161 timestamp, signed-certificate identity).
    $signScript = Join-Path $PSScriptRoot 'sign-release.ps1'
    if ($invokeSigning) {
        $signOutput = & $signScript -ExePath $ArtifactExe
        if ($LASTEXITCODE -ne 0) {
            throw "sign-release.ps1 exited with $LASTEXITCODE; signing infrastructure failure."
        }
        if ([string]::IsNullOrWhiteSpace($signOutput)) {
            throw 'sign-release.ps1 produced no structured result; signing infrastructure failure.'
        }
        $signResult = $signOutput | ConvertFrom-Json
        $manifest.signingStatus = $signResult.Status
        $manifest.signatureVerification = $signResult.Verification
        $manifest.externalGates.signingCredentials = $signResult.Status
        $manifest.signingProvider = if ($signResult.Provider) { [string]$signResult.Provider } else { 'not-configured' }
        $manifest.signingKeyProtection = if ($signResult.KeyProtection) { [string]$signResult.KeyProtection } else { 'NOT_CONFIGURED' }
        $manifest.timestampStatus = if ($signResult.TimestampStatus) { [string]$signResult.TimestampStatus } else { 'NOT_PERFORMED' }
        $manifest.signingCertificateSubject = $signResult.CertificateSubject
        $manifest.signingCertificateThumbprint = $signResult.CertificateThumbprint
        $manifest.signingCertificateIssuer = $signResult.CertificateIssuer
        $manifest.signingCertificateSerialNumber = $signResult.CertificateSerialNumber
        $manifest.signingCertificateValidFrom = $signResult.CertificateValidFrom
        $manifest.signingCertificateValidTo = $signResult.CertificateValidTo
        $manifest.signingCertificateEku = $signResult.CertificateEku
        $manifest.timestampCertificateSubject = $signResult.TimestamperSubject
        $manifest.timestampCertificateThumbprint = $signResult.TimestamperThumbprint
        if ([bool]$signResult.Mock) {
            $manifest.signingMock = $true
            if ($productionGate) {
                throw 'Test-only mock signing is refused under the production gate (RELEASE_PRODUCTION_GATE=true).'
            }
        }
        if ($signResult.FinalSha256) {
            if ($signResult.FinalSha256 -notmatch '^[0-9a-fA-F]{64}$') {
                throw "sign-release.ps1 returned a malformed FinalSha256: '$($signResult.FinalSha256)'"
            }
            $manifest.finalSignedSha256 = $signResult.FinalSha256.ToLowerInvariant()
        }
        if ($productionGate -and $signResult.Status -eq 'SIGNED') {
            if (-not (Test-ApprovedProductionSigningProvider ([string]$manifest.signingProvider))) {
                throw "Production policy requires an approved HSM/cloud signing provider but the signer reported '$($manifest.signingProvider)'."
            }
            if (-not (Test-ApprovedProductionKeyProtection ([string]$manifest.signingKeyProtection))) {
                throw "Production policy requires non-exportable/hardware-backed key protection but the signer reported '$($manifest.signingKeyProtection)'."
            }
            if ([string]$manifest.timestampStatus -ne 'VERIFIED') {
                throw "Production policy requires a verified RFC3161 timestamp but timestampStatus=$($manifest.timestampStatus)."
            }
            if ([string]::IsNullOrWhiteSpace([string]$manifest.signingCertificateThumbprint)) {
                throw 'Production policy requires the signed-certificate identity in the manifest but the signer recorded none.'
            }
            if ([string]::IsNullOrWhiteSpace([string]$manifest.timestampCertificateSubject) -or
                [string]::IsNullOrWhiteSpace([string]$manifest.timestampCertificateThumbprint)) {
                throw 'Production policy requires the RFC3161 timestamper identity in the manifest but the signer recorded none.'
            }
            # The CURRENT trusted publisher policy must equal the signed
            # certificate subject: an artifact cannot carry a publisher the
            # current policy does not approve, even when the manifest
            # consistently records that same publisher.
            $expectedPublisher = [string]$env:SIGNING_EXPECTED_SUBJECT
            if ([string]::IsNullOrWhiteSpace($expectedPublisher)) {
                throw 'Production policy requires the current publisher identity policy (SIGNING_EXPECTED_SUBJECT) but it is not configured.'
            }
            if (-not [string]::Equals([string]$manifest.signingCertificateSubject, $expectedPublisher, [StringComparison]::Ordinal)) {
                throw "Production policy requires the signed certificate subject to equal the current publisher identity policy; manifest records '$($manifest.signingCertificateSubject)' but SIGNING_EXPECTED_SUBJECT is '$expectedPublisher'."
            }
        }
        if ($signingRequired -and $signResult.Status -ne 'SIGNED') {
            throw "Production policy requires signing but the result was $($signResult.Status)"
        }
    }
    elseif ($signingRequired) {
        throw 'Production policy requires signing (RELEASE_SIGNING_REQUIRED=true or RELEASE_PRODUCTION_GATE=true) but no SIGNING_PROVIDER is configured.'
    }

    # --- Release manifest and checksums (FINAL distributed hash) -------------
    # artifactSha256 and SHA256SUMS.txt are computed from the artifact AS IT
    # EXISTS NOW (after signing). Complete-ReleaseRecords fails closed when
    # the on-disk hash disagrees with finalSignedSha256 (signed path) or
    # unsignedQualifiedSha256 (unsigned path), then proves
    # file == manifest.artifactSha256 == SHA256SUMS.txt.
    $manifest.qualificationStatus = 'PASS'
    $manifest.qualificationTimestamp = [DateTimeOffset]::UtcNow.ToString('O')
    $records = Complete-ReleaseRecords -Manifest $manifest -ArtifactPath $ArtifactExe -OutDir $OutDir
    $manifestPath = $records.ManifestPath
    $sumsPath = $records.SumsPath

    $verdict = 'PASS'
    Write-Host ''
    Write-Host 'Release qualification: PASS' -ForegroundColor Green
    Write-Host "Unsigned qualified SHA-256: $($manifest.unsignedQualifiedSha256)" -ForegroundColor Green
    if ($manifest.finalSignedSha256) {
        Write-Host "Final signed SHA-256: $($manifest.finalSignedSha256)" -ForegroundColor Green
        Write-Host "Signing provider: $($manifest.signingProvider) (key protection $($manifest.signingKeyProtection), timestamp $($manifest.timestampStatus))" -ForegroundColor Green
        if ($manifest.signingCertificateSubject) {
            Write-Host "Signing certificate subject: $($manifest.signingCertificateSubject)" -ForegroundColor Green
        }
    }
    Write-Host "Final distributed SHA-256 (artifactSha256 == SHA256SUMS.txt): $($records.ArtifactSha256)" -ForegroundColor Green
    Write-Host "Manifest: $manifestPath" -ForegroundColor Green
    Write-Host "Checksums: $sumsPath" -ForegroundColor Green
    [IO.File]::ReadAllText($manifestPath)
    }
    catch {
        $failureReason = $_.Exception.Message
        Write-Host ''
        Write-Host "Release qualification: FAIL - $failureReason" -ForegroundColor Red
        exit 1
    }
}
finally {
    Pop-Location
    if ($verdict -ne 'PASS' -and [string]::IsNullOrWhiteSpace($failureReason)) {
        Write-Host ''
        Write-Host 'Release qualification: FAIL' -ForegroundColor Red
        exit 1
    }
}
