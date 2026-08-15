<#
.SYNOPSIS
    Authenticode signing for the TabDock release executable through a pluggable
    signing-provider abstraction.

.DESCRIPTION
    The signing backend is selected with the SIGNING_PROVIDER environment
    variable. Every provider reports the SAME structured result contract so
    release-qualify.ps1 and the workflows never know or care HOW signing is
    performed:

      SIGNING_PROVIDER      Backend                          Key protection
      -------------------   ------------------------------   --------------
      (empty)               nothing configured               NOT_CONFIGURED
      local-pfx             exportable PFX + password        LOCAL_PFX
                            (development/private/enterprise
                            trust only - NEVER production)
      digicert-stm          DigiCert Software Trust Manager  CLOUD_HSM
                            cloud signing (non-exportable
                            key held by the DigiCert
                            service/HSM) - the approved
                            production backend
      mock-test             test-only mock signer            MOCK_TEST
                            (deterministic regression
                            scaffolding, never production)

    Provider material (CI secrets/environment only, never committed):

      local-pfx:    SIGNCERT_BASE64   base64-encoded PFX (an EXPORTABLE
                                     private key; development/private use
                                     only - not the approved public-GA
                                     signer)
                    SIGNCERT_PASSWORD PFX password
                    SIGNCERT_TIMESTAMP optional RFC3161 timestamp URL
                                     (default http://timestamp.digicert.com)

      digicert-stm: SM_HOST          DigiCert ONE / Software Trust Manager
                                     environment URL (repository variable)
                    SM_API_KEY       API key for the service user (secret)
                    SM_CLIENT_CERT_FILE  path of the client-authentication
                                     .p12 certificate materialized on the
                                     runner from SM_CLIENT_CERT_FILE_B64
                                     (secret) by the workflow. This
                                     authenticates TO the signing service;
                                     it is NOT the production code-signing
                                     private key, which never leaves the
                                     DigiCert service/HSM.
                    SM_CLIENT_CERT_PASSWORD  password of that .p12 (secret)
                    SM_KEYPAIR_ALIAS keypair alias to sign with
                                     (repository variable)
                    SIGNING_EXPECTED_SUBJECT expected publisher identity
                                     (repository variable); MANDATORY for
                                     production candidates
                                     (RELEASE_PRODUCTION_GATE=true) and, when
                                     configured on any path, the signed
                                     certificate subject must match it
                                     exactly; stable across certificate
                                     rotation (subject identity, never a
                                     hard-coded thumbprint)

      digicert-stm signing uses the official DigiCert smctl tooling
      (installed by the digicert/code-signing-software-trust-action setup
      step in the prepare-release-candidate workflow, pinned to the full
      immutable SHA
      fae23a455ba4bde62b64fd7cb2f81ade788f5a95 / v1.2.1) with the
      current official simple-signing invocation:

          smctl sign --simple --input <exe> --keypair-alias <alias>
                     --digalg sha256 --exit-non-zero-on-fail

      (matching digicert/code-signing-software-trust-action@v1's
      smctl_signing.ts; timestamping stays enabled). SMCTL_PATH may
      override the smctl lookup for development/testing.

    States (emitted as a JSON object):
      NOT_CONFIGURED         no provider selected; nothing was signed
      BLOCKED_EXTERNAL       a provider IS selected but its credentials are
                             incomplete/missing; nothing was signed
      SIGNED                 signed successfully
      SIGNATURE_VERIFIED     signed AND independent signtool verification
                             passed
      SIGNING_FAILED         signing or verification failed
      (the JSON also carries "Mock": true when a test-only mock mode ran)

    Result contract (all providers):
      Status, Verification, FinalSha256, Provider, KeyProtection,
      TimestampStatus, CertificateSubject, CertificateThumbprint,
      CertificateIssuer, CertificateSerialNumber, CertificateValidFrom,
      CertificateValidTo, CertificateEku, TimestamperSubject,
      TimestamperThumbprint, [Mock]

    TEST-ONLY MOCK MODES (never used by production):
      -MockSign           appends deterministic bytes to the executable
                          (modeling "Authenticode signing changes the bytes"),
                          reports SIGNED/SIGNATURE_VERIFIED with the final
                          hash, and sets Mock=true. NO Authenticode signature
                          is applied; the artifact is NOT verifiable by
                          signtool and can never pass the production
                          publication gate. Mock results always report
                          Provider=mock-test / KeyProtection=MOCK_TEST and
                          can never claim an approved production provider.
      -MockSignFailure    reports SIGNING_FAILED and exits 3 without touching
                          the file (models a signer failure).
      -MockVerifyFailure  mutates the file like -MockSign but reports
                          verification FAILED and exits 3 (models a signature
                          that signed but failed verification).
      Mock modes REQUIRE SIGNING_PROVIDER=mock-test, refuse to run while any
      real provider material is configured, and are refused by the
      production gate in scripts/release-qualify.ps1.

    Provider policy (shared with scripts/release-tooling.ps1):
      Production candidates (RELEASE_PRODUCTION_GATE=true) require an
      APPROVED production provider (currently digicert-stm, key protection
      class CLOUD_HSM - the private key is non-exportable). local-pfx is
      NEVER the approved public-GA signer: deleting the temporary PFX after
      signing does not make an exportable-key model equivalent to HSM-backed
      key protection. If RELEASE_SIGNING_REQUIRED=true and no provider
      material is present, the script exits non-zero.

    The PFX (local-pfx) is written to a random temp file, used once, and
    deleted in a finally block; the password is passed to signtool through a
    temporary environment variable, never printed, never stored. Provider
    credential VALUES are never printed - only variable names.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ExePath,
    [switch]$MockSign,
    [switch]$MockSignFailure,
    [switch]$MockVerifyFailure
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'release-tooling.ps1')

$provider = Get-SigningProvider
$mockMode = $MockSign -or $MockSignFailure -or $MockVerifyFailure
$signingRequired = [string]::Equals($env:RELEASE_SIGNING_REQUIRED, 'true', [StringComparison]::OrdinalIgnoreCase)

$status = 'NOT_CONFIGURED'
$verification = 'NOT_PERFORMED'
$finalSha256 = $null
$timestampStatus = 'NOT_PERFORMED'
$certSubject = $null
$certThumbprint = $null
$certIssuer = $null
$certSerialNumber = $null
$certValidFrom = $null
$certValidTo = $null
$certEku = $null
$timestampSubject = $null
$timestampThumbprint = $null

function Emit-Result {
    $result = [ordered]@{
        Status                = $status
        Verification          = $verification
        FinalSha256           = $finalSha256
        Provider              = $provider
        KeyProtection         = (Get-SigningProviderKeyProtection $provider)
        TimestampStatus       = $timestampStatus
        CertificateSubject    = $certSubject
        CertificateThumbprint = $certThumbprint
        CertificateIssuer     = $certIssuer
        CertificateSerialNumber = $certSerialNumber
        CertificateValidFrom  = $certValidFrom
        CertificateValidTo    = $certValidTo
        CertificateEku        = $certEku
        TimestamperSubject    = $timestampSubject
        TimestamperThumbprint = $timestampThumbprint
    }
    if ($mockMode) {
        $result['Mock'] = $true
    }
    $result | ConvertTo-Json -Compress
}

function Complete-RealSignerValidation {
    <#
    .SYNOPSIS
        Provider-independent post-sign validation, shared by every real
        provider: independent Authenticode verification, RFC3161 timestamp
        verification, signed-certificate extraction with the code-signing
        EKU check and the optional expected-subject policy, then the FINAL
        hash. Sets the result fields and returns the process exit code the
        script must use (0 = success, 3 = any failure). The provider
        reporting success is never sufficient; Windows must validate the
        actual bytes.
    #>
    if (-not (Test-AuthenticodeSignature $ExePath)) {
        $script:status = 'SIGNED'
        $script:verification = 'FAILED'
        Write-Host 'sign-release: provider reported success but independent Authenticode verification FAILED (signtool verify /pa).' -ForegroundColor Red
        Emit-Result
        return 3
    }
    $script:verification = 'SIGNATURE_VERIFIED'

    if (Test-AuthenticodeTimestamp $ExePath) {
        $script:timestampStatus = 'VERIFIED'
    }
    else {
        $script:timestampStatus = 'FAILED'
        Write-Host 'sign-release: RFC3161 timestamp is absent or invalid; timestamp policy is mandatory, the run fails.' -ForegroundColor Red
        Emit-Result
        return 3
    }

    $certInfo = Get-SignerCertificateInfo $ExePath
    if ($null -eq $certInfo) {
        $script:status = 'SIGNING_FAILED'
        Write-Host 'sign-release: no valid signed certificate could be read from the executable after signing.' -ForegroundColor Red
        Emit-Result
        return 3
    }
    if (-not (Test-CertificateEkuIncludesCodeSigning $certInfo.Eku)) {
        $script:status = 'SIGNING_FAILED'
        Write-Host "sign-release: the signing certificate does not include the code-signing EKU (1.3.6.1.5.5.7.3.3): $($certInfo.Eku -join ', ')" -ForegroundColor Red
        Emit-Result
        return 3
    }
    $expectedSubject = [string]$env:SIGNING_EXPECTED_SUBJECT
    $productionGate = [string]::Equals($env:RELEASE_PRODUCTION_GATE, 'true', [StringComparison]::OrdinalIgnoreCase)
    if ($productionGate -and [string]::IsNullOrWhiteSpace($expectedSubject)) {
        # Mandatory for the production signer path: the CURRENT publisher
        # identity policy must be configured so the signed certificate is
        # bound to current policy, never merely to whatever the manifest says.
        $script:status = 'SIGNING_FAILED'
        Write-Host 'sign-release: SIGNING_EXPECTED_SUBJECT (the current production publisher identity policy) is not configured; production signing requires the expected publisher subject.' -ForegroundColor Red
        Emit-Result
        return 3
    }
    if (-not [string]::IsNullOrWhiteSpace($expectedSubject) -and $certInfo.Subject -ne $expectedSubject) {
        $script:status = 'SIGNING_FAILED'
        Write-Host 'sign-release: SIGNING_EXPECTED_SUBJECT does not match the signing certificate subject.' -ForegroundColor Red
        Emit-Result
        return 3
    }
    if ([string]::IsNullOrWhiteSpace([string]$certInfo.TimestamperSubject) -or
        [string]::IsNullOrWhiteSpace([string]$certInfo.TimestamperThumbprint)) {
        $script:status = 'SIGNING_FAILED'
        Write-Host 'sign-release: no RFC3161 timestamper identity could be read from the signed executable; timestamp provenance cannot be recorded.' -ForegroundColor Red
        Emit-Result
        return 3
    }

    $script:certSubject = $certInfo.Subject
    $script:certThumbprint = $certInfo.Thumbprint
    $script:certIssuer = $certInfo.Issuer
    $script:certSerialNumber = $certInfo.SerialNumber
    $script:certValidFrom = $certInfo.ValidFrom
    $script:certValidTo = $certInfo.ValidTo
    $script:certEku = @($certInfo.Eku)
    $script:timestampSubject = $certInfo.TimestamperSubject
    $script:timestampThumbprint = $certInfo.TimestamperThumbprint
    $script:finalSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $ExePath).Hash.ToLowerInvariant()
    $script:status = 'SIGNED'
    return 0
}

if (-not (Test-Path -LiteralPath $ExePath -PathType Leaf)) {
    throw "sign-release: executable not found: $ExePath"
}

# ---------------------------------------------------------------------------
# TEST-ONLY MOCK MODES (SIGNING_PROVIDER=mock-test only)
# ---------------------------------------------------------------------------
if ($mockMode) {
    if ($provider -ne 'mock-test') {
        throw "sign-release: test-only mock modes require SIGNING_PROVIDER=mock-test (got '$provider'); mock signing must never be selectable in a real signing configuration."
    }
    $mockCount = @($MockSign, $MockSignFailure, $MockVerifyFailure | Where-Object { $_ }).Count
    if ($mockCount -gt 1) {
        throw 'sign-release: -MockSign, -MockSignFailure, and -MockVerifyFailure are mutually exclusive.'
    }
    $realMaterial = @('SIGNCERT_BASE64', 'SIGNCERT_PASSWORD', 'SM_HOST', 'SM_API_KEY', 'SM_CLIENT_CERT_FILE', 'SM_CLIENT_CERT_PASSWORD', 'SM_KEYPAIR_ALIAS') |
        Where-Object { -not [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($_)) }
    if ($realMaterial.Count -gt 0) {
        throw "sign-release: test-only mock modes refuse to run while real provider material is set ($($realMaterial -join ', ')); never mix mock and real material."
    }
    Write-Host 'sign-release: TEST-ONLY MOCK MODE - no Authenticode signing is performed; this is test scaffolding and Mock=true is recorded.' -ForegroundColor Magenta
    if ($MockSignFailure) {
        $status = 'SIGNING_FAILED'
        Write-Host 'sign-release: mock sign failure (test-only)' -ForegroundColor Red
        Emit-Result
        exit 3
    }

    # Model the real-world fact that Authenticode signing changes the artifact
    # bytes: append a deterministic content-derived marker so the post-sign
    # hash differs while remaining reproducible for the same input file.
    $original = [IO.File]::ReadAllBytes($ExePath)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $digest = $sha.ComputeHash($original) } finally { $sha.Dispose() }
    $suffix = [Text.Encoding]::ASCII.GetBytes('MOCKSIGN:' + [Convert]::ToHexString($digest, 0, 8))
    $mutated = [byte[]]::new($original.Length + $suffix.Length)
    [Array]::Copy($original, $mutated, $original.Length)
    [Array]::Copy($suffix, 0, $mutated, $original.Length, $suffix.Length)
    [IO.File]::WriteAllBytes($ExePath, $mutated)

    if ($MockVerifyFailure) {
        $status = 'SIGNED'
        $verification = 'FAILED'
        Write-Host 'sign-release: mock signature verification failure (test-only)' -ForegroundColor Red
        Emit-Result
        exit 3
    }
    $status = 'SIGNED'
    $verification = 'SIGNATURE_VERIFIED'
    $finalSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $ExePath).Hash.ToLowerInvariant()
    Write-Host "sign-release: mock SIGNED (test-only, Mock=true, Provider=mock-test); final SHA-256 = $finalSha256" -ForegroundColor Green
    Emit-Result
    exit 0
}

# ---------------------------------------------------------------------------
# NOT_CONFIGURED (no provider selected)
# ---------------------------------------------------------------------------
if ($provider -eq 'not-configured') {
    if ($signingRequired) {
        Write-Host 'sign-release: RELEASE_SIGNING_REQUIRED=true but SIGNING_PROVIDER is not configured.' -ForegroundColor Red
        Emit-Result
        exit 2
    }
    Write-Host 'sign-release: no signing provider configured; status=NOT_CONFIGURED' -ForegroundColor Yellow
    Emit-Result
    exit 0
}

if ($provider -eq 'mock-test') {
    throw "sign-release: SIGNING_PROVIDER=mock-test requires an explicit -MockSign/-MockSignFailure/-MockVerifyFailure test flag; mock signing is test scaffolding only."
}

$signtool = Find-Signtool
if ($null -eq $signtool) {
    $status = 'SIGNING_FAILED'
    Write-Host 'sign-release: signtool.exe not found (Windows SDK required for Authenticode signing).' -ForegroundColor Red
    Emit-Result
    exit 3
}

# ---------------------------------------------------------------------------
# LOCAL-PFX provider (development/private/enterprise trust ONLY - never the
# approved public-GA signer; the private key is EXPORTABLE)
# ---------------------------------------------------------------------------
if ($provider -eq 'local-pfx') {
    $base64 = [Environment]::GetEnvironmentVariable('SIGNCERT_BASE64')
    $password = [Environment]::GetEnvironmentVariable('SIGNCERT_PASSWORD')
    if ([string]::IsNullOrWhiteSpace($base64)) {
        $status = 'BLOCKED_EXTERNAL'
        Write-Host 'sign-release: SIGNING_PROVIDER=local-pfx but SIGNCERT_BASE64 is not configured. Local-PFX is NOT the approved public-GA signer; production candidates require digicert-stm.' -ForegroundColor Red
        Emit-Result
        exit 2
    }
    if ([string]::IsNullOrWhiteSpace($password)) {
        $status = 'BLOCKED_EXTERNAL'
        Write-Host 'sign-release: SIGNING_PROVIDER=local-pfx but SIGNCERT_PASSWORD is not configured; refusing to guess a password.' -ForegroundColor Red
        Emit-Result
        exit 2
    }
    $timestampUrl = if ($env:SIGNCERT_TIMESTAMP) { $env:SIGNCERT_TIMESTAMP } else { 'http://timestamp.digicert.com' }

    $tempPfx = Join-Path ([IO.Path]::GetTempPath()) ("TabDock-cert-" + [Guid]::NewGuid().ToString('N') + '.pfx')
    $previousEnv = $null
    try {
        [IO.File]::WriteAllBytes($tempPfx, [Convert]::FromBase64String($base64))
        $previousEnv = $env:TABDOCK_SIGN_PASSWORD
        $env:TABDOCK_SIGN_PASSWORD = $password

        # Sign with SHA-256 digest and an explicit RFC3161 timestamp.
        $signArgs = @('sign', '/fd', 'sha256', '/f', $tempPfx, '/p', $env:TABDOCK_SIGN_PASSWORD,
            '/tr', $timestampUrl, '/td', 'sha256', $ExePath)
        & $signtool @signArgs
        if ($LASTEXITCODE -ne 0) {
            $status = 'SIGNING_FAILED'
            Write-Host "sign-release: signtool sign failed (exit $LASTEXITCODE)" -ForegroundColor Red
            Emit-Result
            exit 3
        }
        Write-Host 'sign-release: local-PFX signing completed; running provider-independent validation.' -ForegroundColor Yellow
    }
    finally {
        if ($null -ne $previousEnv) { $env:TABDOCK_SIGN_PASSWORD = $previousEnv }
        else { Remove-Item Env:\TABDOCK_SIGN_PASSWORD -ErrorAction SilentlyContinue }
        if (Test-Path -LiteralPath $tempPfx) {
            Remove-Item -LiteralPath $tempPfx -Force -ErrorAction SilentlyContinue
        }
    }
    $exitCode = Complete-RealSignerValidation
    if ($exitCode -ne 0) { exit $exitCode }
    Write-Host "sign-release: local-PFX SIGNED + SIGNATURE_VERIFIED; final SHA-256 = $finalSha256" -ForegroundColor Green
    Emit-Result
    exit 0
}

# ---------------------------------------------------------------------------
# DIGICERT-STM provider (approved production backend: non-exportable key held
# by the DigiCert Software Trust Manager service/HSM; the runner only ever
# holds authentication material for the service)
# ---------------------------------------------------------------------------
if ($provider -eq 'digicert-stm') {
    $cfg = Test-SigningProviderConfiguration 'digicert-stm'
    if (-not $cfg.Configured) {
        $status = 'BLOCKED_EXTERNAL'
        Write-Host "sign-release: SIGNING_PROVIDER=digicert-stm is missing required configuration: $($cfg.Missing -join ', '). These authenticate to the DigiCert signing service; they never contain the production code-signing private key." -ForegroundColor Red
        Emit-Result
        exit 2
    }

    $smctlPath = [string]$env:SMCTL_PATH
    if ([string]::IsNullOrWhiteSpace($smctlPath)) {
        $smctlCommand = Get-Command smctl -ErrorAction SilentlyContinue
        if ($null -ne $smctlCommand) { $smctlPath = $smctlCommand.Source }
    }
    if ([string]::IsNullOrWhiteSpace($smctlPath) -or -not (Test-Path -LiteralPath $smctlPath -PathType Leaf)) {
        $status = 'SIGNING_FAILED'
        Write-Host 'sign-release: digicert-stm requires the official DigiCert smctl tool, which was not found on PATH. The prepare-release-candidate workflow installs it via the digicert/code-signing-software-trust-action setup step pinned to fae23a455ba4bde62b64fd7cb2f81ade788f5a95 (v1.2.1, simple-signing-mode); SMCTL_PATH may override the lookup for development/testing.' -ForegroundColor Red
        Emit-Result
        exit 3
    }

    $keypairAlias = [Environment]::GetEnvironmentVariable('SM_KEYPAIR_ALIAS')
    # Current official simple-signing invocation, mirroring
    # digicert/code-signing-software-trust-action smctl_signing.ts
    # (action pinned to fae23a455ba4bde62b64fd7cb2f81ade788f5a95 / v1.2.1):
    #   smctl sign --simple --input <path> --keypair-alias <alias>
    #          [--digalg sha256] [--exit-non-zero-on-fail]
    # Timestamping stays enabled (the service timestamps by default); the
    # independent timestamp verification below is mandatory.
    $signArgs = @('sign', '--simple', '--input', $ExePath, '--keypair-alias', $keypairAlias,
        '--digalg', 'sha256', '--exit-non-zero-on-fail')
    Write-Host "sign-release: invoking smctl ($smctlPath) with the official simple-signing invocation..." -ForegroundColor Cyan
    $smctlOutput = & $smctlPath @signArgs 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        $status = 'SIGNING_FAILED'
        Write-Host "sign-release: smctl signing failed (exit $LASTEXITCODE); the cloud signing operation did not succeed." -ForegroundColor Red
        Write-Host $smctlOutput
        Emit-Result
        exit 3
    }
    Write-Host $smctlOutput
    Write-Host 'sign-release: DigiCert STM signing completed; running provider-independent validation.' -ForegroundColor Yellow

    $exitCode = Complete-RealSignerValidation
    if ($exitCode -ne 0) { exit $exitCode }
    Write-Host "sign-release: digicert-stm SIGNED + SIGNATURE_VERIFIED (CLOUD_HSM); final SHA-256 = $finalSha256" -ForegroundColor Green
    Emit-Result
    exit 0
}

# Unreachable: Get-SigningProvider only returns the four known providers.
throw "sign-release: unexpected signing provider '$provider'."
