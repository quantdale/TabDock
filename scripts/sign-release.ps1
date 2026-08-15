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
    [string]$ExePath
)

$ErrorActionPreference = 'Stop'

$status = 'NOT_CONFIGURED'
$verification = 'NOT_PERFORMED'
$finalSha256 = $null

function Emit-Result {
    $result = [ordered]@{
        Status       = $status
        Verification = $verification
        FinalSha256  = $finalSha256
    }
    $result | ConvertTo-Json -Compress
}

$base64 = $env:SIGNCERT_BASE64
$password = $env:SIGNCERT_PASSWORD
$timestampUrl = if ($env:SIGNCERT_TIMESTAMP) { $env:SIGNCERT_TIMESTAMP } else { 'http://timestamp.digicert.com' }
$signingRequired = [string]::Equals($env:RELEASE_SIGNING_REQUIRED, 'true', [StringComparison]::OrdinalIgnoreCase)

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

# Locate signtool from the installed Windows SDK.
function Find-Signtool {
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
