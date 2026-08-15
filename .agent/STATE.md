# TabDock agent state

## Git authority

Git is authoritative for `HEAD`, branch, `origin/main`, and worktree state.
Resolve them dynamically at every fresh session. This file never records a
self-referential current SHA or a hosted-CI result for the commit containing
this text.

## Current checkpoint — production release closure (v1.0.0 campaign)

- Objective: finish the legacy-audit semantic reconciliation (Campaign A) and
  build the strongest defensible v1.0.0 release engineering (Campaign B).
  Campaign A is COMPLETE: PR #10 was closed without merging, the
  `agent/tabdock-deep-audit-remediation` branch was deleted, and the remote
  branch set contains `main` only.
- Campaign C (release-pipeline correctness remediation, this session):
  - External production evidence is now enforced, not documented:
    `release-external-evidence.json` (schemaVersion 1, sourceCommitSha,
    artifactSha256, finalWindowsHumanSmoke + physicalMixedDpi each with
    status/operator/completedAt/evidence). The release workflow's publish
    job validates it via the shared gate and refuses publication when
    evidence is missing/malformed/wrong-SHA/wrong-hash/FAIL/BLOCKED_EXTERNAL.
    Qualification-only runs never need evidence.
  - Checksum-ordering defect fixed: `artifactSha256` and `SHA256SUMS.txt`
    ALWAYS describe the FINAL distributed executable (post-sign);
    `unsignedQualifiedSha256` retains pre-sign provenance; triple
    consistency (file == manifest == SHA256SUMS.txt) is enforced in both
    release qualification and the publish job.
  - `scripts/release-tooling.ps1` shared module (final-hash selection,
    checksums, evidence validation, publication gate, signtool
    discovery/verify). `scripts/release-tooling-tests.ps1` = 37 deterministic
    adversarial regression cases using test-only mock signer modes
    (`sign-release.ps1 -MockSign/-MockSignFailure/-MockVerifyFailure`,
    Mock=true, never with real material, never production-eligible).
  - Production signing policy explicit: `create-release=true` forces
    RELEASE_SIGNING_REQUIRED + RELEASE_PRODUCTION_GATE; the publish job
    additionally requires SIGNED + SIGNATURE_VERIFIED + finalSignedSha256 ==
    final artifact hash + independent `signtool verify /pa`. RC qualification
    may stay NOT_CONFIGURED.
  - WINDOWPLACEMENT: 44-byte runtime contract preserved; `--selftest-native-abi`
    added (structure/offsets, get/set round trip, zero-length and 60-byte
    rejection, per-machine environment report); `build.yml` gains a
    `native-abi-evidence` job on windows-2022 as a second OS build;
    `docs/release/compatibility-matrix.md` records proven vs EXTERNAL
    environments (Windows 10 x64 remains untested/external).
  - Docs reconciled: `docs/release/publication-gates.md` (trust model,
    evidence schema, production dispatch walkthrough), updated
    `final-smoke.md`/`mixed-dpi-qualification.md`/`repository-protection.md`/
    README. OpenSpec change `production-release-v1-0-0-closure` extended
    with the remediation tasks and delta requirements.
- Campaign B release engineering (repository side complete, with Campaign C
  corrections):
  - `scripts/release-qualify.ps1` — exact-SHA + clean-tree enforcement,
    audited restore, single publish, qualification of the published
    executable (embedded commit == candidate SHA; self-reported SHA-256 ==
    `Get-FileHash`; geometry/diagnostics/native-ABI self-tests on that
    binary), `release-manifest.json` + `SHA256SUMS.txt` written from the
    FINAL hash; vocabulary PASS/FAIL/BLOCKED_EXTERNAL/BLOCKED_ENVIRONMENT
    with explicit `externalGates` and `productionReleaseEligibility`.
  - `scripts/sign-release.ps1` — signing-ready, secret-only material,
    `NOT_CONFIGURED`/`SIGNED`/`SIGNATURE_VERIFIED`/`SIGNING_FAILED`,
    `RELEASE_SIGNING_REQUIRED` enforcement, temp-PFX lifecycle, RFC3161
    timestamping, test-only mock modes for regression tests. No
    certificate/credentials in the repository; actual signing is
    `BLOCKED_EXTERNAL`.
  - `.github/workflows/release.yml` — dispatch-only, exact `sha` input,
    checkout verification, canonical qualification, immutable artifact
    retention (`upload-artifact@v7`), and a publication job that runs the
    fail-closed gate (manifest PASS, exact SHA/version, artifact triple
    consistency, schema-valid evidence, mandatory signing + independent
    `signtool verify /pa`) before `gh release create`, then verifies assets
    including `release-external-evidence.json` and
    `publication-verification.json`. No stable release on push; no second
    compilation at publication.
  - `global.json` — .NET 8 SDK feature band with `rollForward: latestFeature`.
    NuGet lock mode deliberately avoided; NuGet audit mandatory in CI;
    OpenSpec pinned via `npm ci` lockfile.
  - `docs/release/mixed-dpi-qualification.md` (16 physical scenarios,
    `BLOCKED_NO_MIXED_DPI_HARDWARE` result) and
    `docs/release/final-smoke.md` (38 manual checks, operator-signed
    evidence, exact signed artifact requirement) — both procedures exist;
    execution is `BLOCKED_EXTERNAL` until real evidence exists.
  - GitHub ruleset `release-tags` (id 20878779, active): `refs/tags/v*`
    cannot be deleted or force-pushed. Main branch protection deliberately
    NOT applied (would deadlock the validated direct-push workflow); see
    `docs/release/repository-protection.md`.
  - OpenSpec change `production-release-v1-0-0-closure` with granular
    tasks; unchecked items (real signing, manual smoke, physical mixed-DPI,
    release publication, ruleset application beyond tags) are explicitly
    NOT done.

## Validation and external qualification

- Canonical repository qualification is
  `scripts/validate.ps1 -Configuration Release -Ci -Publish`, including
  audited restores, Release solution/ValidationDriver/GuineaPig/Performance
  builds, deterministic geometry/diagnostics/persistence/privacy checks,
  OpenSpec, recovery smoke, support-bundle privacy, publish, and exact build
  identity. Hosted Actions evidence is always resolved dynamically for the
  exact SHA; it is not persisted here.
- Release-tooling regression suite:
  `scripts/release-tooling-tests.ps1` (37 cases; no real certificates, no
  publishing) — run before every release-pipeline change.
- The release chain is `scripts/release-qualify.ps1 -Ci -Sha <sha> -Version
  1.0.0 -Sign` (locally: without `-Ci`); it fails on SHA mismatch, dirty
  trees, published-exe identity mismatch, or self-report/hash mismatch, and
  writes manifest + checksums from the FINAL artifact hash.
- Do not automate shutdown/logoff. Do not claim mixed-DPI hardware,
  unavailable-browser, or foreground-policy qualification without evidence.
- The final release decision requires real evidence for: final manual
  Windows smoke (`docs/release/final-smoke.md`), physical mixed-DPI
  qualification (`docs/release/mixed-dpi-qualification.md`), the signing
  credentials, and the Windows 10 x64 compatibility item
  (`docs/release/compatibility-matrix.md`). Their PASS results must be
  recorded in `release-external-evidence.json`
  (`docs/release/publication-gates.md`). Without them the honest verdict is
  GO FOR RELEASE CANDIDATE ONLY, and v1.0.0 is PREPARED BUT INTENTIONALLY
  NOT PUBLISHED.

## Resume

1. Read `AGENTS.md`, this file, `docs/ARCHITECTURE.md`, `docs/TESTING.md`,
   `docs/release/*`, and the active `production-release-v1-0-0-closure`
   OpenSpec change.
2. Resolve Git and hosted CI dynamically. Preserve unrelated work and never
   reset/clean/force-push published `main`. `v*` tags are protected by the
   `release-tags` ruleset.
3. The remaining v1.0.0 work is external-gate evidence (human smoke,
   mixed-DPI hardware, signing credentials, Windows 10 x64 compatibility),
   then an intentional `release.yml` production run against the exact final
   SHA with `release-external-evidence.json`; do not publish v1.0.0 without
   it.
4. Do not create a state-only commit merely to record a SHA or CI run.
