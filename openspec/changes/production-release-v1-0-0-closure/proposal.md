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
  canonical qualification, artifact retention, optional intentional
  publication consuming the preserved artifact with re-verification).
- Add an auditable external-gate evidence mechanism:
  `release-external-evidence.json` records the final human Windows smoke and
  the physical mixed-DPI qualification, bound to the exact candidate SHA and
  the FINAL artifact hash; production publication is refused until the record
  is schema-valid and both gates PASS. Qualification-only runs never need
  evidence.
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
