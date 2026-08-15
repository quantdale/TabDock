# DigiCert cloud signing research (2026-08-15)

Verification of the CURRENT official DigiCert cloud/HSM signing tooling for
the production signer abstraction (Campaign E). Files in this directory are
verbatim copies of the official action's README/source at the time of
research.

## Findings

- Official GitHub Action: `digicert/code-signing-software-trust-action@v1`
  (marketplace name "DigiCert Binary Signing"; latest release v1.2.1,
  repository updated 2026-08-05). Source: `digicert/code-signing-software-trust-action`.
- Product: DigiCert Software Trust Manager (STM), part of DigiCert ONE.
  KeyLocker-backed HSM keypairs; the code-signing private key is
  non-exportable and stays inside the service.
- Authentication environment variables (current official naming):
  - `SM_HOST` (environment URL; use a GitHub Actions variable)
  - `SM_API_KEY` (service-user API key; use a secret)
  - `SM_CLIENT_CERT_FILE` (path of the client-authentication .p12; the
    README recommends storing the file content base64 in a secret,
    e.g. `SM_CLIENT_CERT_FILE_B64`, and decoding it on the runner)
  - `SM_CLIENT_CERT_PASSWORD` (password of that .p12; use a secret)
  - Keypair selection: action input `keypair-alias`.
- Simple signing mode (`simple-signing-mode: true`) installs `smctl` and
  adds its directory to PATH (`tool_setup.ts`: `core.addPath(toolPath)`).
  Without `input` + `keypair-alias` inputs the action only performs setup
  and does NOT sign (`smctl_signing.ts` returns early).
- Current official simple-signing invocation (from `smctl_signing.ts`):

  ```
  smctl sign --simple --input <path> --keypair-alias <alias>
             [--timestamp=false] [--digalg <alg>] [--exit-non-zero-on-fail]
  ```

  Note: the action passes the digest flag as `--digalg` (official spelling
  in the action source). Timestamping defaults to enabled; the STM service
  timestamps the signature.
- Legacy (non-simple) mode installs the full smtools + PKCS#11 module and
  outputs `PKCS11_CONFIG` for third-party-tool signing (e.g. SignTool
  with `/c`). We intentionally use simple-signing mode (recommended by
  DigiCert for new implementations).
- The action verifies the SHA-256 checksum of downloaded tools from the
  DigiCert CDN before installation (supply-chain protection).

## Design consequences implemented

- `sign-release.ps1` `digicert-stm` provider invokes
  `smctl sign --simple --input <exe> --keypair-alias <alias> --digalg sha256
  --exit-non-zero-on-fail`, mirroring the official action source.
- `prepare-release-candidate.yml` runs the official action in setup-only
  mode and materializes the client certificate from `SM_CLIENT_CERT_FILE_B64`.
- Verification (signature, RFC3161 timestamp, certificate identity) is
  provider-independent Windows tooling.

## References

- Action README (this directory, `action-readme.md`) and official docs:
  https://docs.digicert.com/en/software-trust-manager.html
- Product page: https://www.digicert.com/software-trust-manager
- Action source files copied here: `action.yml`, `smctl_signing.ts`,
  `tool_setup.ts`.
