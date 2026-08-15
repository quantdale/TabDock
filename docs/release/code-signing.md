# TabDock Code Signing Architecture

**Status: the provider abstraction and production policy are implemented; no
real production signing has occurred — `BLOCKED_EXTERNAL` until the approved
signing service credentials are configured and Stage A signs the one real
candidate.**

This document explains how TabDock's production pipeline signs the release
executable with a modern **cloud/HSM signing provider** (non-exportable
private key), why local-PFX signing is not the public-GA path, and how the
signer abstraction keeps the two-stage release chain (Stage A prepare, human
qualification, Stage B publish) byte-exact. No secret values are documented
here — only variable **names**, providers, and policy.

## 1. Why public-GA signing uses hardware/cloud-backed private keys

Publicly trusted Windows code-signing certificates are issued under
programs (Microsoft Trusted Root / Windows Hardware Dev Center policy) that
increasingly require the private key to live in **compliant hardware
cryptographic modules (HSMs)** or an **HSM-backed signing service**. The
industry model is:

- the private key is **non-exportable** — it is generated inside and never
  leaves the HSM/signing service;
- signing happens **inside the service** (the service signs a digest of the
  file and returns the signature);
- the CI runner never holds a file that contains the private key.

An exportable PFX in a GitHub Secret or on a runner is the wrong model for
this: whoever reads the secret can copy the private key and sign anything as
TabDock. Deleting the temporary PFX after signing does **not** make that
model equivalent to HSM-backed protection — the key was still exportable,
and the security of the whole supply chain depended on a secret value that
was present on the runner.

TabDock therefore treats the **private-key protection class** as the
production gate: production candidates require `CLOUD_HSM` (key held by the
signing service/HSM, non-exportable). `LOCAL_PFX` (exportable key) is never
approved for production, even when the artifact is genuinely
Authenticode-signed by a real certificate.

## 2. Why local PFX is not the public production path

The existing PFX path (`SIGNCERT_BASE64` + `SIGNCERT_PASSWORD`, signed with
`signtool /f /p`) remains available and useful for:

- local/private certificates,
- developer testing,
- enterprise/private trust,
- backwards compatibility with RC qualification.

It is explicitly reclassified as `SIGNING_PROVIDER=local-pfx`, documented as
**NOT THE APPROVED PUBLIC-GA SIGNER**, and rejected by production policy:

| Layer | local-pfx behavior |
|---|---|
| Stage A preflight | `BLOCKED_EXTERNAL`: provider not approved |
| `release-qualify.ps1` with `RELEASE_PRODUCTION_GATE=true` | fails before any build |
| Stage B publication gate | rejects `signingProvider=local-pfx` / `signingKeyProtection=LOCAL_PFX` |
| Documentation | `LOCAL PFX SIGNING IS NOT THE APPROVED PUBLIC-GA SIGNER.` |

## 3. Supported signing providers/backends

The signer is selected with the **`SIGNING_PROVIDER`** environment variable
(repository variable in the workflows). Every provider returns the same
structured contract to `release-qualify.ps1`, so the pipeline does not know
or care how signing happens.

| `SIGNING_PROVIDER` | Backend | Key protection | Production-approved |
|---|---|---|---|
| *(empty)* `not-configured` | nothing configured; artifact stays unsigned | `NOT_CONFIGURED` | no |
| `local-pfx` | exportable PFX + password via `signtool /f /p` (dev/private/enterprise trust only) | `LOCAL_PFX` | no |
| `digicert-stm` | DigiCert Software Trust Manager cloud signing (official DigiCert tooling; non-exportable key inside the DigiCert service/HSM) | `CLOUD_HSM` | **yes (currently the only approved provider)** |
| `mock-test` | test-only deterministic mock signer (`-MockSign*` flags) | `MOCK_TEST` | no |

The approved production provider allowlist lives in
`Get-ApprovedProductionSigningProviders` (currently exactly `digicert-stm`)
and the approved key-protection class in `Get-ApprovedProductionKeyProtection`
(currently exactly `CLOUD_HSM`). Adding another provider (for example
**Microsoft Artifact Signing**) is a policy change in exactly those two
functions plus a provider adapter in `sign-release.ps1`; Microsoft Artifact
Signing is deliberately NOT implemented here — its Public Trust eligibility
is geography-restricted and the pipeline must not depend on one
geographically restricted service.

### 3.1 DigiCert Software Trust Manager (`digicert-stm`)

Implemented against the **current official DigiCert tooling** (verified
against `digicert/code-signing-software-trust-action@v1`, release v1.2.1,
August 2026):

- the workflow runs the official action in **setup-only** mode
  (`simple-signing-mode: true`, no `input`/`keypair-alias`), which installs
  the official `smctl` CLI and adds it to `PATH`;
- `sign-release.ps1` performs the single signing operation with the official
  simple-signing invocation (mirroring the action's `smctl_signing.ts`):

  ```
  smctl sign --simple --input <exe> --keypair-alias <alias>
             --digalg sha256 --exit-non-zero-on-fail
  ```

- timestamping stays enabled (DigiCert RFC3161) and is verified
  independently afterwards (section 6).

The DigiCert STM **keypair** is created inside the DigiCert ONE / Software
Trust Manager account (KeyLocker-backed HSM). The runner only ever holds:

- the public certificate material,
- the client-authentication certificate (`.p12`, service-user auth),
- the API key (service-user auth),
- the `SM_HOST` URL and the keypair alias,
- short-lived authentication material.

The production code-signing private key never exists as a file on the
runner, never exists in GitHub Secrets, and is never committed.

## 4. Required GitHub configuration per provider

### `digicert-stm` (production)

| Variable | GitHub kind | Meaning |
|---|---|---|
| `SIGNING_PROVIDER` | repository variable | must be `digicert-stm` for production |
| `SM_HOST` | repository variable | DigiCert ONE / Software Trust Manager environment URL |
| `SM_API_KEY` | secret | API key of the service user |
| `SM_CLIENT_CERT_FILE_B64` | secret | base64 of the client-authentication `.p12` (materialized on the runner by the workflow; **authentication** material, not the code-signing key) |
| `SM_CLIENT_CERT_PASSWORD` | secret | password of that `.p12` |
| `SM_KEYPAIR_ALIAS` | repository variable | keypair alias to sign with |
| `SIGNING_EXPECTED_SUBJECT` | repository variable, optional | expected publisher identity (certificate subject); when set it must match exactly, adding a publisher allowlist without hard-coding a thumbprint |
| `SIGNCERT_TIMESTAMP` | secret, optional | timestamp URL override (default DigiCert RFC3161) |

### `local-pfx` (development/private/RC only — NOT the public-GA signer)

| Variable | GitHub kind | Meaning |
|---|---|---|
| `SIGNING_PROVIDER` | repository variable / RC input | `local-pfx` |
| `SIGNCERT_BASE64` | secret | base64-encoded **exportable** PFX |
| `SIGNCERT_PASSWORD` | secret | PFX password |
| `SIGNCERT_TIMESTAMP` | secret, optional | RFC3161 timestamp URL |

Never commit any of these values. The pipeline prints only variable **names**.

## 5. How Stage A signs

Stage A (`prepare-release-candidate.yml`) after the canonical qualification:

1. **Preflight (provider-aware, before any build):** resolves
   `SIGNING_PROVIDER`, requires an **approved** production provider
   (`digicert-stm`) and complete provider credentials — otherwise the run
   fails with `BLOCKED_EXTERNAL` and no candidate is produced. Only missing
   variable names are reported, never values.
2. **DigiCert tooling setup** (conditional on `digicert-stm`): materializes
   the client-authentication certificate from `SM_CLIENT_CERT_FILE_B64` and
   runs the official `digicert/code-signing-software-trust-action@v1`
   setup step.
3. `release-qualify.ps1` publishes the exact source SHA once, qualifies the
   exact binary, then calls `sign-release.ps1` **once** with
   `SIGNING_PROVIDER=digicert-stm`, which invokes `smctl` against the
   signing service. The private key stays inside the service.
4. The signer result (structured JSON: status, verification, final hash,
   provider, key protection, timestamp status, certificate identity) is
   folded into `release-manifest.json`.

Production candidates are never built or signed twice, and Stage A never
creates a GitHub Release.

## 6. How Stage A verifies

The provider reporting success is **never sufficient**. After the signing
operation, provider-independent Windows verification runs against the actual
EXE:

- `signtool verify /pa /v` — Windows independently validates the
  Authenticode signature and its certificate chain;
- RFC3161 timestamp verification — `signtool verify /pa` plus a visible
  timestamp certificate on the signature (`Test-AuthenticodeTimestamp`);
  passing `/tr` is never treated as proof of timestamping; a missing or
  invalid timestamp **fails** Stage A (`timestampStatus=FAILED`);
- signed-certificate identity — subject, thumbprint, issuer, serial number,
  validity window, and EKU are read from the file with
  `Get-AuthenticodeSignature`; the code-signing EKU
  (`1.3.6.1.5.5.7.3.3`) is mandatory; when `SIGNING_EXPECTED_SUBJECT` is
  configured it must match;
- only then is the FINAL SHA-256 computed (`finalSignedSha256`), the
  manifest finalized, and `SHA256SUMS.txt` written from the final bytes.

## 7. How final hash/provenance is recorded

`release-manifest.json` records the signing provenance of the final
distributed bytes:

| Field | Meaning |
|---|---|
| `signingStatus` | `SIGNED` (or `NOT_CONFIGURED` / `SIGNING_FAILED` / `BLOCKED_EXTERNAL`) |
| `signatureVerification` | `SIGNATURE_VERIFIED` only after independent verification |
| `signingProvider` | e.g. `digicert-stm` |
| `signingKeyProtection` | e.g. `CLOUD_HSM` (production-approved class) |
| `timestampStatus` | `VERIFIED` / `FAILED` / `NOT_PERFORMED` |
| `finalSignedSha256` | hash AFTER signing |
| `unsignedQualifiedSha256` | pre-sign provenance hash |
| `artifactSha256` / `SHA256SUMS.txt` | hash of the FINAL distributed executable |
| `signingCertificateSubject` / `Thumbprint` / `Issuer` / `SerialNumber` | certificate identity (forensic value; the thumbprint is recorded, not hard-coded) |
| `signingCertificateValidFrom` / `ValidTo` | certificate validity window |
| `signingCertificateEku` | EKU OIDs (must include code signing) |
| `signingMock` | `true` only for test-only mock runs (never production) |

`release-tooling.ps1` enforces file == manifest == `SHA256SUMS.txt` and the
Stage B gate (section 8) requires the approved provider class.

## 8. How Stage B remains provider-independent

Stage B (`publish-release.yml`) **never contacts the signing provider and
never re-signs**. It downloads the exact Stage A artifact and validates the
already-signed immutable bytes using only Windows signature verification and
the retained provenance:

- run/artifact/SHA/version/hash binding (unchanged);
- manifest `signingProvider` must be an approved production provider and
  `signingKeyProtection` must be approved (`CLOUD_HSM`) — `local-pfx`,
  `mock`, `not-configured`, and unknown signers are rejected;
- `signingStatus=SIGNED`, `signatureVerification=SIGNATURE_VERIFIED`,
  `finalSignedSha256` == final artifact hash, no `signingMock`;
- certificate identity recorded in the manifest (subject, thumbprint,
  issuer, validity window, code-signing EKU);
- `timestampStatus=VERIFIED`;
- independent `signtool verify /pa` + RFC3161 timestamp verification of the
  downloaded bytes, with the certificate identity cross-checked against the
  manifest.

No signing credentials are required in Stage B, so publication never depends
on live access to the provider.

## 9. What BLOCKED_EXTERNAL means

`BLOCKED_EXTERNAL` is the honest verdict when a required external condition
is missing. In signing it means: **a production signer is required (or was
selected) but cannot run** — the provider is not configured
(`SIGNING_PROVIDER` empty), is not an approved production provider
(`local-pfx`, `mock-test`, unknown), or is approved but missing required
credentials (only the variable **names** are reported). Stage A fails with
`BLOCKED_EXTERNAL` **before any build**, so a missing signer can never
produce a candidate. The release status remains **GO FOR RELEASE CANDIDATE
/ BETA ONLY** until one real signed Stage A artifact exists via the approved
production signer and the human qualification gates have real evidence.
