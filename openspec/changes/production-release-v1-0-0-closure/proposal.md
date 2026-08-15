## Why

TabDock is at a strong release-candidate state but has no production release
chain: qualified publish output was never retained as an artifact, no release
manifest/checksums existed, no Authenticode signing path existed, the version
contract was not enforced end-to-end, and the .NET SDK selection was
implicit. Without these, any future "v1.0.0" would be an unattributable
binary. This change builds the release engineering so a future v1.0.0 is
exact-SHA attributable, immutable, and honest about which external gates are
still missing.

## What Changes

- Add an exact-SHA release qualification tool
  (`scripts/release-qualify.ps1`) that enforces a clean exact commit, audited
  restore, single publish, qualification of the published executable itself,
  SHA-256 computation, and a machine-readable `release-manifest.json` plus
  `SHA256SUMS.txt`.
- Add a signing-ready integration (`scripts/sign-release.ps1`) with explicit
  `NOT_CONFIGURED` / `SIGNED` / `SIGNATURE_VERIFIED` / `SIGNING_FAILED`
  states, secret-based material only, timestamping configuration, and no
  repository-stored credentials.
- Add a fail-closed `release.yml` workflow (dispatch-only, exact commit,
  canonical qualification, artifact retention) restricted to RC
  qualification; publication moves to the two-stage chain.
- Add the two-stage production chain: `prepare-release-candidate.yml`
  (Stage A) builds once, Authenticode-signs once (mandatory, `BLOCKED_EXTERNAL`
  without credentials), and retains the immutable candidate; `publish-release.yml`
  (Stage B) takes the Stage A run id, downloads the EXACT retained artifact
  cross-run, re-verifies every production condition against those bytes, and
  publishes them with the derived tag `v<semanticVersion>` — no rebuild, no
  re-sign, no tag input, and the published SHA equals the evidence
  `artifactSha256`.
- Add an auditable external-gate evidence mechanism:
  `release-external-evidence.json` (schemaVersion 2) records the final human
  Windows smoke, the physical mixed-DPI qualification, and the Windows 10/11
  x64 compatibility qualification, bound to the exact candidate SHA, the
  FINAL artifact hash, the Stage A run id, and the candidate artifact name;
  production publication is refused until the record is schema-valid and all
  gates PASS. Qualification-only runs never need evidence.
- Make the project version the single authority: `TabDock.csproj <Version>`
  is read from the exact candidate source, workflow version inputs are
  EXPECTED values that must agree, the binary's reported and informational
  versions must carry it, and the manifest records it.
- Add the physical mixed-DPI qualification procedure, the final manual
  Windows smoke procedure, and the Windows 10/11 compatibility gate as
  required human gates that remain explicitly unperformed until real
  evidence exists.
- Correct the final-hash checksum contract: `artifactSha256` and
  `SHA256SUMS.txt` always describe the FINAL distributed executable (after
  signing, when signing occurs), `unsignedQualifiedSha256` retains the
  pre-sign provenance hash, and file == manifest == checksums is enforced in
  both qualification and publication. Add deterministic release-tooling
  regression tests (including adversarial cases) that exercise the
  signing-path semantics without real certificate material.
- Make the production signing policy explicit: production publication
  requires Authenticode `SIGNED` + `SIGNATURE_VERIFIED` with an independent
  `signtool verify /pa`; RC qualification may remain unsigned.
- Pin the .NET SDK via `global.json` (8.0 feature band, roll-forward within
  .NET 8 only) without reintroducing the deliberately-avoided NuGet lock
  mode; document the reproducibility policy.
- Add the physical mixed-DPI qualification procedure and the final manual
  Windows smoke procedure as required human gates that remain explicitly
  unperformed until real evidence exists.
- Normalize the version contract: `TabDock.csproj` `Version` remains the
  authoritative mechanism; release tooling validates agreement and records it
  in the manifest.
- Retire the superseded legacy audit branch (PR #10) after semantic
  reconciliation and exact-SHA validation, leaving `main` as the only remote
  branch.

## Non-goals

- No actual v1.0.0 publication in this change unless every required
  external gate (manual smoke, physical mixed-DPI, signing policy) has real
  evidence. A release candidate is preferable to a falsely-qualified GA.
- No reintroduction of NuGet lock files (deliberately avoided; see the
  performance-optimization change history).
- No changes to capture/recovery/identity production behavior.
