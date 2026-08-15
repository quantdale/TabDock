<#
.SYNOPSIS
    Optional Authenticode signing for the TabDock release executable.

.DESCRIPTION
    Signing-ready integration that never stores certificates or passwords in
    the repository. Material arrives via CI secrets/environment:

      SIGNCERT_BASE64    base64-encoded PFX (the only supported input format;
                         generated from GitHub Actions secrets)
      SIGNCERT_PASSWORD  PFX password
      SIGNCERT_TIMESTAMP optional RFC3161 timestamp URL (default DigiCert)

    States (emitted as a JSON object):
      NOT_CONFIGURED         no material present; nothing was signed
      SIGNED                 signed successfully
      SIGNATURE_VERIFIED     signed AND signtool verification passed
      SIGNING_FAILED         signing or verification failed
      (the JSON also carries "Mock": true when a test-only mock mode ran)

    TEST-ONLY MOCK MODES (never used by production):
      -MockSign           appends deterministic bytes to the executable
                          (modeling "Authenticode signing changes the bytes"),
                          reports SIGNED/SIGNATURE_VERIFIED with the final
                          hash, and sets Mock=true. NO Authenticode signature
                          is applied; the artifact is NOT verifiable by
                          signtool and can never pass the production
                          publication gate (signingMock is recorded and
                          rejected there).
      -MockSignFailure    reports SIGNING_FAILED and exits 3 without touching
                          the file (models a signtool sign failure).
      -MockVerifyFailure  mutates the file like -MockSign but reports
                          verification FAILED and exits 3 (models a signature
                          that signed but failed verification).
      Mock modes refuse to run while SIGNCERT_BASE64 is set and are refused by
      the production gate in scripts/release-qualify.ps1.

    If RELEASE_SIGNING_REQUIRED=true and material is absent, the script exits
    non-zero (release policy is mandatory-signing); otherwise it reports
    NOT_CONFIGURED and exits 0.

    The PFX is written to a random temp file, used once, and deleted in a
    finally block. The password is passed to signtool through a temporary
    environment variable, never printed, never stored.
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

$status = 'NOT_CONFIGURED'
$verification = 'NOT_PERFORMED'
$finalSha256 = $null
$mockMode = $MockSign -or $MockSignFailure -or $MockVerifyFailure

function Emit-Result {
    $result = [ordered]@{
        Status       = $status
        Verification = $verification
        FinalSha256  = $finalSha256
    }
    if ($mockMode) {
        $result['Mock'] = $true
    }
    $result | ConvertTo-Json -Compress
}

$base64 = $env:SIGNCERT_BASE64
$password = $env:SIGNCERT_PASSWORD
$timestampUrl = if ($env:SIGNCERT_TIMESTAMP) { $env:SIGNCERT_TIMESTAMP } else { 'http://timestamp.digicert.com' }
$signingRequired = [string]::Equals($env:RELEASE_SIGNING_REQUIRED, 'true', [StringComparison]::OrdinalIgnoreCase)

if ($mockMode) {
    $mockCount = @($MockSign, $MockSignFailure, $MockVerifyFailure | Where-Object { $_ }).Count
    if ($mockCount -gt 1) {
        throw 'sign-release: -MockSign, -MockSignFailure, and -MockVerifyFailure are mutually exclusive.'
    }
    if (-not [string]::IsNullOrWhiteSpace($base64)) {
        throw 'sign-release: test-only mock modes refuse to run while SIGNCERT_BASE64 is set (never mix mock and real material).'
    }
    Write-Host 'sign-release: TEST-ONLY MOCK MODE - no Authenticode signing is performed; this is test scaffolding and Mock=true is recorded.' -ForegroundColor Magenta
    if (-not (Test-Path -LiteralPath $ExePath -PathType Leaf)) {
        throw "sign-release: executable not found: $ExePath"
    }
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
    Write-Host "sign-release: mock SIGNED (test-only, Mock=true); final SHA-256 = $finalSha256" -ForegroundColor Green
    Emit-Result
    exit 0
}

if ([string]::IsNullOrWhiteSpace($base64)) {
    if ($signingRequired) {
        Write-Host 'sign-release: RELEASE_SIGNING_REQUIRED=true but SIGNCERT_BASE64 is not configured.' -ForegroundColor Red
        Emit-Result
        exit 2
    }
    Write-Host 'sign-release: no signing material configured; status=NOT_CONFIGURED' -ForegroundColor Yellow
    Emit-Result
    exit 0
}

if ([string]::IsNullOrWhiteSpace($password)) {
    if ($signingRequired) {
        Write-Host 'sign-release: RELEASE_SIGNING_REQUIRED=true but SIGNCERT_PASSWORD is not configured.' -ForegroundColor Red
        Emit-Result
        exit 2
    }
    Write-Host 'sign-release: SIGNCERT_BASE64 present without SIGNCERT_PASSWORD; refusing to guess a password.' -ForegroundColor Red
    Emit-Result
    exit 2
}

if (-not (Test-Path -LiteralPath $ExePath -PathType Leaf)) {
    throw "sign-release: executable not found: $ExePath"
}

$signtool = Find-Signtool
if ($null -eq $signtool) {
    throw 'sign-release: signtool.exe not found (Windows SDK required for Authenticode signing).'
}

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

    # Verify the signature chain (Authenticode, not just embedded).
    $verifyArgs = @('verify', '/pa', '/v', $ExePath)
    & $signtool @verifyArgs
    if ($LASTEXITCODE -ne 0) {
        $status = 'SIGNED'
        $verification = 'FAILED'
        Write-Host 'sign-release: signing completed but signtool verify failed.' -ForegroundColor Red
        Emit-Result
        exit 3
    }

    $status = 'SIGNED'
    $verification = 'SIGNATURE_VERIFIED'
    $finalSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $ExePath).Hash.ToLowerInvariant()
    Write-Host "sign-release: SIGNED + SIGNATURE_VERIFIED; final SHA-256 = $finalSha256" -ForegroundColor Green
    Emit-Result
}
finally {
    if ($null -ne $previousEnv) { $env:TABDOCK_SIGN_PASSWORD = $previousEnv }
    else { Remove-Item Env:\TABDOCK_SIGN_PASSWORD -ErrorAction SilentlyContinue }
    if (Test-Path -LiteralPath $tempPfx) {
        Remove-Item -LiteralPath $tempPfx -Force -ErrorAction SilentlyContinue
    }
}
