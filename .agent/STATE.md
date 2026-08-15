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
- Legacy reconciliation outcome: every legacy correction was already present
  or superseded on main except three, now reconciled directly:
  - `GetWindowPlacement` is declared `ref` (caller-initialized `length` must
    reach user32; mirrors `SetWindowPlacement`), and the empirically-validated
    44-byte `WINDOWPLACEMENT` contract is documented and locked in by
    `NativeInteropSelfTest` (size/offset checks plus a native get/set round
    trip on a self-created never-shown window, zero-length rejection, and
    length write-back). Modern Windows 10/11 user32 never populates the SDK
    header's trailing `rcDevice`, writes the accepted size back as 44, and
    rejects `length == 60` with `ERROR_INVALID_PARAMETER` — verified on
    Windows 11 10.0.26200 and consistent with the 44-byte round-trip length
    reported across Windows 10/11. Declaring `rcDevice` would have silently
    broken every placement restore.
  - `ShowWindow` no longer declares `SetLastError` (its BOOL is previous
    visibility); post-state verification semantics were already present.
  - Diagnostics now report build-corrected `ProductName`, `ProductFamily`,
    and raw registry `RawProductName` (Windows 11 builds with stale
    "Windows 10" registry branding are no longer mislabeled; real Windows 10
    stays identifiable). Deterministic coverage added.
- Campaign B release engineering (repository side complete):
  - `scripts/release-qualify.ps1` — exact-SHA + clean-tree enforcement,
    audited restore, single publish, qualification of the published
    executable (embedded commit == candidate SHA; self-reported SHA-256 ==
    `Get-FileHash`; geometry/diagnostics self-tests on that binary),
    `release-manifest.json` + `SHA256SUMS.txt`; qualification vocabulary
    PASS/FAIL/BLOCKED_EXTERNAL/BLOCKED_ENVIRONMENT with explicit
    `externalGates`.
  - `scripts/sign-release.ps1` — signing-ready, secret-only material,
    `NOT_CONFIGURED`/`SIGNED`/`SIGNATURE_VERIFIED`/`SIGNING_FAILED`,
    `RELEASE_SIGNING_REQUIRED` enforcement, temp-PFX lifecycle, RFC3161
    timestamping. No certificate/credentials in the repository; actual
    signing is `BLOCKED_EXTERNAL`.
  - `.github/workflows/release.yml` — dispatch-only, exact `sha` input,
    checkout verification, canonical qualification, immutable artifact
    retention (`upload-artifact@v7`), and an explicit publication job that
    re-verifies provenance before `gh release create` and verifies assets.
    No stable release on push; no second compilation at publication.
  - `global.json` — .NET 8 SDK feature band with `rollForward: latestFeature`
    (stays within .NET 8). NuGet lock mode remains deliberately avoided
    (SDK-generated ILLink.Tasks instability); NuGet audit remains mandatory
    in CI; OpenSpec stays pinned via `npm ci` lockfile.
  - `docs/release/mixed-dpi-qualification.md` (16 physical scenarios,
    `BLOCKED_NO_MIXED_DPI_HARDWARE` result) and
    `docs/release/final-smoke.md` (38 manual checks, operator-signed
    evidence) — both procedures exist; execution is `BLOCKED_EXTERNAL` until
    real evidence exists.
  - GitHub ruleset `release-tags` (id 20878779, active): `refs/tags/v*`
    cannot be deleted or force-pushed. Main branch protection deliberately
    NOT applied (would deadlock the validated direct-push workflow); see
    `docs/release/repository-protection.md`.
  - OpenSpec change `production-release-v1-0-0-closure` created with
    granular tasks; unchecked items (real signing, manual smoke, physical
    mixed-DPI, release publication, ruleset application beyond tags) are
    explicitly NOT done.

## Validation and external qualification

- Canonical repository qualification is
  `scripts/validate.ps1 -Configuration Release -Ci -Publish`, including
  audited restores, Release solution/ValidationDriver/GuineaPig/Performance
  builds, deterministic geometry/diagnostics/persistence/privacy checks,
  OpenSpec, recovery smoke, support-bundle privacy, publish, and exact build
  identity. Hosted Actions evidence is always resolved dynamically for the
  exact SHA; it is not persisted here.
- The release chain is `scripts/release-qualify.ps1 -Ci -Sha <sha> -Version
  1.0.0 -Sign` (locally: without `-Ci`); it fails on SHA mismatch, dirty
  trees, published-exe identity mismatch, or self-report/hash mismatch.
- Do not automate shutdown/logoff. Do not claim mixed-DPI hardware,
  unavailable-browser, or foreground-policy qualification without evidence.
- The final release decision requires real evidence for: final manual
  Windows smoke (`docs/release/final-smoke.md`), physical mixed-DPI
  qualification (`docs/release/mixed-dpi-qualification.md`), and the signing
  policy. Without them the honest verdict is GO FOR RELEASE CANDIDATE ONLY,
  and v1.0.0 is PREPARED BUT INTENTIONALLY NOT PUBLISHED.

## Resume

1. Read `AGENTS.md`, this file, `docs/ARCHITECTURE.md`, `docs/TESTING.md`,
   `docs/release/*`, and the active `production-release-v1-0-0-closure`
   OpenSpec change.
2. Resolve Git and hosted CI dynamically. Preserve unrelated work and never
   reset/clean/force-push published `main`. `v*` tags are protected by the
   `release-tags` ruleset.
3. The remaining v1.0.0 work is external-gate evidence (human smoke,
   mixed-DPI hardware, signing credentials), then an intentional
   `release.yml` run against the exact final SHA; do not publish v1.0.0
   without it.
4. Do not create a state-only commit merely to record a SHA or CI run.
