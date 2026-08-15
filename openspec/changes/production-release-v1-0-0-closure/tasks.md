## 1. Exact-SHA artifact chain

- [x] 1.1 Add `scripts/release-qualify.ps1`: exact-SHA + clean-tree enforcement, audited restore, single-file publish, execution and qualification of the published executable (version identity equals candidate SHA, self-reported SHA-256 matches `Get-FileHash`, geometry and diagnostics self-tests), `release-manifest.json`, and `SHA256SUMS.txt`.
- [x] 1.2 Record in the manifest: product, semantic version, source commit SHA, artifact filename, artifact SHA-256, unsigned qualified SHA-256, final signed SHA-256 when applicable, runtime identifier, configuration, build identity, signing status, signature verification, qualification status, timestamp, workflow run id, and explicit external gates.
- [x] 1.3 Add `.github/workflows/release.yml`: dispatch-only, exact-SHA checkout verification, canonical qualification, artifact retention, and a separate explicit publication job that re-verifies provenance before `gh release create` and verifies assets afterwards.
- [x] 1.4 Do NOT rebuild at publication time; the release consumes the preserved qualified artifact.

## 2. Signing readiness

- [x] 2.1 Add `scripts/sign-release.ps1` with `NOT_CONFIGURED` / `SIGNED` / `SIGNATURE_VERIFIED` / `SIGNING_FAILED` states, secret-only material, explicit RFC3161 timestamping, temp-PFX lifecycle, and `RELEASE_SIGNING_REQUIRED` enforcement.
- [x] 2.2 Record signing status and (when applicable) both hash identities in the manifest; never describe an unsigned executable as signed.
- [ ] 2.3 Actual Authenticode signing with real material — NOT DONE: credentials are `BLOCKED_EXTERNAL`; infrastructure is ready and safe to leave unsigned.

## 3. Version consistency

- [x] 3.1 Document `TabDock.csproj` `Version` as the authoritative mechanism; release tooling records the expected version and validates it appears in the manifest; release notes render from the manifest.
- [x] 3.2 Confirm `--version`, `BuildIdentity`, informational version, and the manifest agree for a produced artifact.

## 4. Release workflow safety

- [x] 4.1 No stable release on every push: `release.yml` is `workflow_dispatch`-only.
- [x] 4.2 Fail-closed publication: manifest `PASS` + matching SHA + on-disk hash equality required before release; tag created at the exact candidate SHA; assets verified after publication.
- [x] 4.3 Actions versions are current supported majors matching the repository's existing green pins (`checkout@v7`, `setup-dotnet@v6`, `setup-node@v7`, `upload-artifact@v7`, `download-artifact@v7`).

## 5. Dependency and reproducibility policy

- [x] 5.1 Add `global.json` pinning the .NET 8 SDK feature band with `rollForward: latestFeature`; verify local build and CI honor it.
- [x] 5.2 Keep NuGet lock mode avoided (documented rationale: SDK-generated ILLink.Tasks instability); keep mandatory NuGet audit in CI; keep OpenSpec pinned via `npm ci` lockfile.
- [x] 5.3 Document the reproducibility policy in `docs/ARCHITECTURE.md` and README.

## 6. Human/external qualification procedures

- [x] 6.1 Add `docs/release/mixed-dpi-qualification.md` (16 scenarios, evidence requirements, `BLOCKED_NO_MIXED_DPI_HARDWARE` result).
- [x] 6.2 Add `docs/release/final-smoke.md` (38 checks, operator-signed evidence, exact-artifact identity requirement).
- [ ] 6.3 Execute the final manual Windows smoke — NOT DONE: `BLOCKED_EXTERNAL` (requires a human on real Windows with the exact artifact).
- [ ] 6.4 Execute physical mixed-DPI qualification — NOT DONE: `BLOCKED_EXTERNAL` (no mixed-DPI hardware evidence).
- [ ] 6.5 Browser qualification for unavailable browsers — NOT DONE: `SKIP_BROWSER_NOT_INSTALLED` reported per browser; no fabricated coverage.

## 7. Known limitations and documentation

- [x] 7.1 Review README known limitations against current behavior (self-maximize, UIPI/elevation, cross-reboot persistence intent, DPI awareness, legacy tokenless recovery, Task Manager kill, browser availability) and update where stale.
- [x] 7.2 Document signing policy, release workflow usage, and artifact provenance in README.
- [x] 7.3 Document the DWM transition-suppression rationale and residual hard-kill behavior in `docs/ARCHITECTURE.md`.

## 8. Legacy audit reconciliation closure

- [x] 8.1 Semantically reconcile `agent/tabdock-deep-audit-remediation` (WINDOWPLACEMENT ref/44-byte contract + ShowWindow declaration + Windows product normalization); all other legacy corrections were already present or superseded.
- [x] 8.2 Run canonical validation (builds, geometry/diagnostics 205 checks, doctor/version, support-bundle privacy, OpenSpec 19/19, publish smoke) — PASS.
- [x] 8.3 Push reconciliation and verify exact-SHA hosted CI.
- [x] 8.4 Close PR #10 without merging and delete the superseded branch once exact-SHA CI is green.
- [x] 8.5 Confirm remote branch set contains `main` only after cleanup (verified via `git branch -r`: `origin/HEAD -> origin/main`, `origin/main`).

## 9. Repository protection policy

- [x] 9.1 Document recommended GitHub settings (release-tag ruleset: no force push, no deletion; optional exact-SHA status requirement for `v*` tags) — actual repository setting mutation is `BLOCKED_ENVIRONMENT` unless API credentials with admin scope are available and the mutation is safe to perform.
- [x] 9.2 Apply the release-tag ruleset — DONE: ruleset `release-tags` (id 20878779, active, tag target, `refs/tags/v*`: deletion and force-push blocked) applied via the repository API with admin scope.

## 10. Final qualification and release decision

- [x] 10.1 Run the full canonical local qualification after all repository-side changes (`validate.ps1 -Configuration Release -Ci -Publish`) — PASS.
- [x] 10.2 Run `git diff --check` and a manual full-diff review for secrets, private keys, signing credentials, prohibited APIs, generated junk, and unrelated churn.
- [x] 10.3 Run `release-qualify.ps1` locally to prove the artifact chain and manifest generation end-to-end.
- [x] 10.4 Push the final production-closure commit and verify the exact final SHA's hosted CI run (must be green, must produce the candidate artifact for that SHA).
- [ ] 10.5 Release decision — NOT DONE in this change: expected verdict is GO FOR RELEASE CANDIDATE ONLY until the manual smoke, physical mixed-DPI, and signing gates have real evidence.

## 11. Release-pipeline correctness remediation (independent review)

- [x] 11.1 External production evidence: `release-external-evidence.json` schema (`schemaVersion`, `sourceCommitSha`, `artifactSha256`, `finalWindowsHumanSmoke`/`physicalMixedDpi` with `status`/`operator`/`completedAt`/`evidence`); the release workflow's publication job schema-validates the evidence and binds it to the exact candidate SHA and the FINAL artifact hash; missing/malformed/wrong-SHA/wrong-hash/`FAIL`/`BLOCKED_EXTERNAL` evidence all fail closed before `gh release create`; qualification-only runs remain possible WITHOUT evidence.
- [x] 11.2 Final-hash checksum contract: `artifactSha256` and `SHA256SUMS.txt` ALWAYS describe the FINAL distributed executable (post-sign); `unsignedQualifiedSha256` retains the pre-sign provenance hash; `finalSignedSha256` recorded when signing changed the bytes; file == manifest == `SHA256SUMS.txt` triple consistency enforced in BOTH release qualification and publication.
- [x] 11.3 Shared tooling module `scripts/release-tooling.ps1` (final-hash selection, checksum generation/parsing, triple consistency, external-evidence validation, the fail-closed publication gate, signtool discovery + `signtool verify /pa`).
- [x] 11.4 Signing-path regression tests without real certificate material: `scripts/release-tooling-tests.ps1` (37 deterministic cases covering the unsigned path, the signed/mutated path, publication provenance, signing failure, signature-verification failure, and all adversarial evidence/gate cases) using test-only mock signer modes in `sign-release.ps1` (`-MockSign`/`-MockSignFailure`/`-MockVerifyFailure`) that never run with real material, are recorded as `Mock=true`, and can never pass the production gate.
- [x] 11.5 Production signing policy: `create-release=true` forces `RELEASE_SIGNING_REQUIRED=true` and `RELEASE_PRODUCTION_GATE=true`; production publication requires `SIGNED` + `SIGNATURE_VERIFIED` + `finalSignedSha256` equal to the final artifact hash + an independent `signtool verify /pa` in the publish job; RC qualification may remain `NOT_CONFIGURED`; an unsigned production release is never silently defaulted.
- [x] 11.6 WINDOWPLACEMENT compatibility evidence: the 44-byte runtime contract is preserved; `--selftest-native-abi` added with per-machine environment report (OS build, accepted length, get/set behavior, 60-byte rejection); the 60-byte rejection is now a hard self-test assertion; `build.yml` runs the native ABI self-test on `windows-2022` as a second OS build; `docs/release/compatibility-matrix.md` documents proven vs unproven Windows environments; Windows 10 x64 remains an external qualification item.
- [x] 11.7 Documentation reconciled: `docs/release/publication-gates.md` (trust model + evidence schema + production dispatch walkthrough), updated `final-smoke.md`/`mixed-dpi-qualification.md` (exact signed artifact + evidence JSON requirements), README release chain corrected; all claims of fail-closed publication now match the enforced gate.

## 12. Two-stage production release architecture (independent review round 2)

- [x] 12.1 Split production publication into two stages with NO rebuild/re-sign between them: `prepare-release-candidate.yml` (Stage A: exact SHA -> qualify -> build once -> Authenticode sign once -> verify -> final hash -> manifest/checksums -> immutable artifact `tabdock-candidate-<sha>-<run-id>`; NEVER creates a GitHub Release; signing mandatory with explicit `BLOCKED_EXTERNAL` preflight) and `publish-release.yml` (Stage B: takes the Stage A run id + schema-v2 evidence, resolves the run and artifact via the GitHub API, downloads the EXACT artifact cross-run with `download-artifact@v7` `run-id`/`repository`/`github-token`, re-verifies everything against the downloaded bytes, publishes those exact bytes with the derived tag). The RC workflow `release.yml` keeps qualification-only semantics and has no publication path.
- [x] 12.2 Stage B fail-closed provenance: source run must exist in this repository, be the `prepare-release-candidate` workflow, be completed/successful; artifact must be `tabdock-candidate-<head-sha>-<run-id>`, live (not expired), and unique; manifest `sourceCommitSha` == run `head_sha`; manifest `workflowRunId` == requested run id; manifest `releaseMode` == PRODUCTION (qualification-only/RC artifacts rejected); evidence must name the same run and artifact.
- [x] 12.3 Version authority: `TabDock.csproj <Version>` is the single authoritative version; `release-qualify.ps1` reads it from the exact candidate source, treats any workflow `-Version` as an EXPECTED value that must agree, records the project version in the manifest, and requires the published executable's reported semantic version and informational version to carry it; the Stage B gate requires manifest version == recorded binary identity == project `<Version>` at the candidate SHA, and re-runs the downloaded `--version`.
- [x] 12.4 Tag authority: the release tag is DERIVED as `v<semanticVersion>` (`Get-ReleaseTagFromVersion`); no free-form tag input exists anywhere in the publication path, so mismatched or arbitrary tags are structurally impossible and the protected `v*` ruleset applies.
- [x] 12.5 Windows compatibility gate: evidence schema v2 adds `windowsCompatibility` with mandatory PASS entries for Windows 10 x64 and Windows 11 x64 (status, OS build, operator, ISO-8601 completedAt, `nativeAbiEvidence` from `--selftest-native-abi`, evidence); missing/FAIL/BLOCKED/malformed Windows 10 or Windows 11 evidence blocks production publication; hosted CI remains proven automatically by every qualification run.
- [x] 12.6 Evidence quality: schemaVersion 2; `candidateWorkflowRunId` (numeric, equals the Stage A run) and `candidateArtifactName` (equals the downloaded artifact) required; all `completedAt` fields must parse as ISO-8601 and not be materially in the future (5-minute tolerance).
- [x] 12.7 Regression tests extended to 69 deterministic cases: version authority chain (expected != project -> FAIL, malformed version -> FAIL, forged binary/informational identity -> FAIL, RC-mode rejection), derived-tag adversarial states, cross-run binding (wrong run id, wrong artifact name, malformed run id, manifest run mismatch, missing artifact), Windows 10/11 gate failures, completedAt quality, and static workflow guarantees (Stage B contains no build/sign/qualify invocation; Stage A forces signing and never publishes; RC workflow has no publication path).
- [ ] 12.8 Execute the real two-stage chain — NOT DONE: `BLOCKED_EXTERNAL` (requires the production Authenticode credential to produce the ONE signed candidate, then human/hardware qualification of that exact artifact, then the Stage B publish dispatch). The Stage A workflow is verifiable only in its blocked state until credentials exist.

## 13. Provider-abstracted signing with a non-exportable production key

- [x] 13.1 Signer abstraction: `SIGNING_PROVIDER` selects the backend (`not-configured` / `local-pfx` / `digicert-stm` / `mock-test`); `scripts/sign-release.ps1` returns ONE structured contract (Status, Verification, FinalSha256, Provider, KeyProtection, TimestampStatus, certificate identity) so `release-qualify.ps1` and the workflows never know how signing happens. Provider selection rejects unknown values; provider credential preflight validates ONLY the selected provider's requirements and reports only variable NAMES (`Test-SigningProviderConfiguration`, `Test-ProductionSigningPreflight`).
- [x] 13.2 Approved production provider policy: allowlist `Get-ApprovedProductionSigningProviders` == `digicert-stm` (DigiCert Software Trust Manager; official `digicert/code-signing-software-trust-action@v1` setup step + official `smctl sign --simple --input <exe> --keypair-alias <alias> --digalg sha256 --exit-non-zero-on-fail` invocation, verified against the current official action source; see `.agent/investigations/digicert-research/`), approved key protection class `CLOUD_HSM` (non-exportable). Production Stage A rejects NOT_CONFIGURED / MOCK / LOCAL_PFX / unknown providers with `BLOCKED_EXTERNAL` BEFORE any build; the production code-signing private key never exists on the runner or in secrets — only service-authentication material (SM_HOST, SM_API_KEY, SM_CLIENT_CERT_FILE from SM_CLIENT_CERT_FILE_B64, SM_CLIENT_CERT_PASSWORD, SM_KEYPAIR_ALIAS). Microsoft Artifact Signing deliberately NOT implemented (geography-restricted Public Trust eligibility); the allowlist makes any future provider an explicit policy change.
- [x] 13.3 local-pfx reclassified (P1): still supported for development/private/enterprise/RC (SIGNCERT_BASE64 + SIGNCERT_PASSWORD + optional SIGNCERT_TIMESTAMP), explicitly documented as NOT the approved public-GA signer, and rejected at every production layer (Stage A preflight, release-qualify production policy, Stage B gate).
- [x] 13.4 Provider-independent verification (P0): after any provider signs, `signtool verify /pa` + RFC3161 timestamp verification (`Test-AuthenticodeTimestamp`; `timestampStatus=VERIFIED` mandatory, missing/invalid timestamp fails Stage A) + signed-certificate identity (`Get-SignerCertificateInfo`: subject, thumbprint, issuer, serial, validity window, EKU incl. code signing 1.3.6.1.5.5.7.3.3) run against the actual EXE; optional `SIGNING_EXPECTED_SUBJECT` publisher allowlist. Provider-reported success is never sufficient.
- [x] 13.5 Manifest provenance (P0/P1): release-manifest.json records `signingProvider`, `signingKeyProtection`, `timestampStatus`, `signingCertificateSubject/Thumbprint/Issuer/SerialNumber/ValidFrom/ValidTo`, `signingCertificateEku` (thumbprint recorded, not hard-coded) beside the existing signingStatus/signatureVerification/finalSignedSha256/unsignedQualifiedSha256/artifactSha256 triple; `publication-verification.json` records signingProviderApproved / signingKeyProtection / signingCertificateIdentity / timestampVerification checks.
- [x] 13.6 Stage B production gate (P0): requires approved `signingProvider` + approved `signingKeyProtection` (rejects local-pfx, mock, not-configured, unknown, missing provider/key-protection metadata), certificate identity + code-signing EKU + validity window, `timestampStatus=VERIFIED`, independent signtool + timestamp verification on the downloaded bytes with certificate identity cross-checked against the manifest; Stage B NEVER contacts the signing provider and NEVER re-signs (no provider credentials anywhere in the workflow, enforced by a static test).
- [x] 13.7 Workflows: `prepare-release-candidate.yml` — provider-aware preflight (approved provider + credential completeness), conditional official DigiCert tooling setup, signing env (SM_*), no release creation, `release.yml` — new `signing-provider` input (`not-configured`/`local-pfx`/`digicert-stm`), `publish-release.yml` — unchanged build/sign-free structure, updated gate comments + verification record + release notes signing lines.
- [x] 13.8 Regression tests extended to 96 deterministic cases (never requiring real credentials): production rejects LOCAL_PFX / NOT_CONFIGURED / MOCK / unknown / missing provider metadata / missing key-protection metadata / LOCAL_PFX key protection; approved CLOUD_HSM metadata accepted by the non-live policy layer; cloud-provider config and tooling failures fail closed (BLOCKED_EXTERNAL, SIGNING_FAILED, artifact untouched); provider-says-signed-but-verification-fails; hash mismatch; mock can never claim an approved provider; timestamp/certificate policy failures; RC unsigned/local-pfx paths; static workflow guarantees incl. zero provider auth in Stage B.
- [x] 13.9 Documentation: new `docs/release/code-signing.md` (why HSM/cloud keys, why local-PFX is not the public path, supported providers, required GitHub configuration per provider, how Stage A signs/verifies, how provenance is recorded, Stage B provider independence, BLOCKED_EXTERNAL); updated `publication-gates.md`, `final-smoke.md`, README, `.agent/STATE.md`, `.agent/decisions/two-stage-production-release.md` (addendum); `.agent/investigations/digicert-research/` captures the current official DigiCert action README/source.
- [ ] 13.10 Real production signing — NOT DONE: `BLOCKED_EXTERNAL` (no approved signer credentials configured; no real signed candidate exists). Release status remains GO FOR RELEASE CANDIDATE / BETA ONLY.
