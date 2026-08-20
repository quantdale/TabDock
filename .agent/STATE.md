# TabDock agent state

## Git authority

Git is authoritative for `HEAD`, branch, `origin/main`, and worktree state.
Resolve them dynamically at every fresh session. This file never records a
self-referential current SHA or a hosted-CI result for the commit containing
this text.

## Runtime stabilization campaign (2026-08, this session)

- Objective: close the live-runtime defects from `tabdock_runtime_audit.md`
  beyond the hotfixes already in `08fc456` (two-handler click path, durable-disk
  I/O on switch, z-order relative order, ensureFinalPass latch, LayoutUpdated
  dirty detection, SWP_FRAMECHANGED removal, WinEvent storm coalescing).
- Done this session:
  - `#3` background state writer — `PersistenceService` split into
    `BuildStateJson`/`CommitJson`; `GroupManager.RequestSave` uses `SaveAsync`
    (off-thread WriteThrough+fsync); same-index `SwitchActiveTab` is a no-op.
  - `#4` zero redundant journal commits — `_durablyJournaledCaptureTokens`
    makes 100 ordinary hides after a durable capture write zero additional
    durable journal entries; intentional-hide invalidates the flag.
  - `#5` `ensureFinalPass` latch only when pending (idle=true→1 pass,
    pending+finalPass→existing+1 follow-up). Verified by
    `RequestRelayoutFinalPassTests`.
  - `#7` relative z-order via `ZOrder.IsOrderedAbove` (ignores IME/helper/overlay
    HWNDs between panes).
  - New `RuntimeStabilizationSelfTest` (runs in `validate.ps1` self-test gate)
    proves `#3`/`#4`/`#7`; added 5 xUnit cases (total 141).
- Validation: Debug+Release build clean; `TabDock.UnitTests` 141 PASS;
  `release-tooling-tests.ps1` 139 PASS; `git diff --check` clean. Live desktop
  A–G and `validate.ps1` full qualification require a real Windows session/CI.
- WM_WINDOWPOSCHANGING investigation: concluded NOT a better signal than
  `WM_WINDOWPOSCHANGED` (fires before final rect; would guess geometry). Residual
  drag jitter after duplicate-work removal is a Shepherd separate-surface
  architecture ceiling, not a callback deficit. No new callbacks added.
- Branch model: the repository is **main-only**. Develop, commit, and push
  directly on `main`; qualify each SHA via `build.yml` on push. `main` is the
  sole authoritative development/integration branch — do NOT recreate an
  `agent/staging` branch or a `promote-staging` workflow.
- Do not reintroduce SetParent/AttachThreadInput/style
  stripping/animations/synthetic activation.

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
    status/operator/completedAt/evidence — superseded by schemaVersion 2 in
    Campaign D below). The release workflow's publish job validates it via
    the shared gate and refuses publication when evidence is
    missing/malformed/wrong-SHA/wrong-hash/FAIL/BLOCKED_EXTERNAL.
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
    Mock=true, never with real material, never production-eligible) —
    extended to 69 cases in Campaign D.
  - Production signing policy explicit: `create-release=true` forces
    RELEASE_SIGNING_REQUIRED + RELEASE_PRODUCTION_GATE (mechanism superseded
    by the Stage A prepare-release-candidate workflow in Campaign D); the
    publish job additionally requires SIGNED + SIGNATURE_VERIFIED +
    finalSignedSha256 == final artifact hash + independent
    `signtool verify /pa`. RC qualification may stay NOT_CONFIGURED.
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
- Campaign D (two-stage production architecture, independent review round 2,
  this session):
  - The release chain is now TWO stages with zero rebuild/re-sign between
    them: `prepare-release-candidate.yml` (Stage A) builds once, signs once
    (mandatory; BLOCKED_EXTERNAL preflight without credentials), verifies,
    computes the final hash, and retains the immutable candidate
    `tabdock-candidate-<sha>-<run-id>` — it never creates a release.
    `publish-release.yml` (Stage B) takes the Stage A run id + schema-v2
    evidence, resolves the run/artifact via the GitHub API (must exist in
    this repo, must be the candidate workflow, completed/successful, artifact
    live and unique), downloads the EXACT artifact cross-run
    (`download-artifact@v7` `run-id`/`repository`/`github-token`), re-verifies
    everything (project version at candidate SHA == manifest == recorded
    binary identity; file == manifest == SHA256SUMS; evidence bound to
    SHA/hash/run/artifact; signtool verify /pa), and publishes those exact
    bytes with the DERIVED tag `v<semanticVersion>` (no tag input exists).
    `release.yml` is now RC qualification-only with no publication path
    (artifact `tabdock-rc-<sha>-<run-id>`, `releaseMode=QUALIFICATION_ONLY`).
  - Version authority: `TabDock.csproj <Version>` is the single authority;
    release-qualify reads it from the exact candidate, the workflow `version`
    input is EXPECTED-only (any disagreement fails), the manifest records the
    project version, and the published binary's semantic + informational
    versions must carry it; the Stage B gate requires manifest == recorded
    binary identity == project version and never executes the downloaded
    binary to re-ask it.
  - Windows compatibility is now a machine-enforced production gate: evidence
    schema v2 requires `windowsCompatibility` with PASS entries for real
    Windows 10 x64 and Windows 11 x64 (status, build, operator, ISO-8601
    completedAt, nativeAbiEvidence from `--selftest-native-abi`, evidence);
    missing/FAIL/BLOCKED blocks publication. Evidence v2 also requires
    `candidateWorkflowRunId` + `candidateArtifactName` and validates every
    completedAt as ISO-8601, not materially future.
  - `scripts/release-tooling-tests.ps1` extended to 69 deterministic cases:
    version authority chain, derived-tag adversarial states, cross-run
    candidate binding, Windows 10/11 gate failures, completedAt quality, and
    static workflow guarantees (Stage B contains no build/sign/qualify
    invocation; Stage A forces signing and never publishes; RC workflow has
    no publication path).
- Campaign E (production signing architecture correction, this session):
  - The signing implementation is now provider-abstracted and the production
    chain no longer requires an exportable PFX: `SIGNING_PROVIDER` selects
    the backend (`not-configured` / `local-pfx` / `digicert-stm` /
    `mock-test`); `sign-release.ps1` returns ONE structured contract (Status,
    Verification, FinalSha256, Provider, KeyProtection, TimestampStatus,
    certificate identity) to release-qualify and the workflows.
  - Approved production provider policy: allowlist == `digicert-stm`
    (DigiCert Software Trust Manager; official
    `digicert/code-signing-software-trust-action@v1` setup-only step +
    official `smctl sign --simple ...` invocation, verified against the
    current official action source captured in
    `.agent/investigations/digicert-research/`), key protection class
    `CLOUD_HSM` (non-exportable). Production Stage A rejects
    NOT_CONFIGURED/MOCK/LOCAL_PFX/unknown via a provider-aware preflight
    that fails BEFORE any build (`BLOCKED_EXTERNAL`, variable names only).
    Microsoft Artifact Signing deliberately NOT implemented
    (geography-restricted Public Trust); the allowlist functions make any
    future provider an explicit policy change.
  - local-pfx reclassified (still available for dev/private/RC, never the
    approved public-GA signer, rejected at every production layer). Mock
    modes now require `SIGNING_PROVIDER=mock-test`, refuse real material,
    and their results can never claim an approved provider.
  - Verification is provider-independent and mandatory: `signtool verify
    /pa`, RFC3161 timestamp verification (`timestampStatus=VERIFIED`; a
    missing/invalid timestamp fails Stage A), and signed-certificate
    identity (subject, thumbprint, issuer, serial, validity, code-signing
    EKU) recorded in the manifest (`signingProvider`,
    `signingKeyProtection`, `timestampStatus`, `signingCertificate*`,
    optional `SIGNING_EXPECTED_SUBJECT` publisher allowlist). Stage B
    requires the approved provider class + key protection + certificate
    identity + verified timestamp, re-runs signtool/timestamp verification
    on the downloaded bytes, cross-checks the certificate identity, and
    NEVER contacts the provider or re-signs.
  - Manifest provenance extended; `release.yml` gained a `signing-provider`
    input (`not-configured`/`local-pfx`/`digicert-stm`, default
    not-configured); `publication-verification.json` gained
    signingProviderApproved / signingKeyProtection /
    signingCertificateIdentity / timestampVerification checks.
  - `scripts/release-tooling-tests.ps1` extended to 96 deterministic cases
    (provider policy, BLOCKED_EXTERNAL config failures, tool-absent failure,
    production rejection matrix, timestamp/certificate policy, mock provider
    discipline, static workflow guarantees incl. no provider auth in Stage B)
    — all PASS locally. New `docs/release/code-signing.md` (9 required
    sections); `publication-gates.md`, `final-smoke.md`, README, decision
    record and OpenSpec change updated.
  - Real production signing remains `BLOCKED_EXTERNAL`: the approved signer
    credentials are not configured and NO real signed candidate exists yet.
    Release status stays GO FOR RELEASE CANDIDATE / BETA ONLY.
- Campaign F (release-policy trust-boundary hardening, independent review
  round 3, this session):
  - P0 trust boundary eliminated: the candidate being evaluated NO LONGER
    supplies the policy that decides whether it may be published. Stage B
    physically separates `policy/` (TRUSTED policy = the executing
    `publish-release.yml` revision; requires `github.ref == refs/heads/main`
    AND `github.workflow_sha == github.sha`; all policy code dot-sourced
    exclusively from `policy/scripts/release-tooling.ps1`),
    `candidate-source/` (data only), and `candidate-artifact/` (data only).
    Stage A production preparation requires the same dispatch contract
    (`inputs.sha == github.sha == main HEAD`) as its FIRST step, before any
    credentials/build; RC qualification still supports arbitrary SHAs.
  - Release-policy schema contract: `releasePolicySchemaVersion` (current 3)
    recorded by Stage A; `Get-MinimumAcceptedProductionPolicySchema` (3)
    makes the CURRENT policy reject absent/stale schemas (old candidates
    fail closed, never evaluated under their own historical policy).
  - Publisher identity policy MANDATORY for production:
    `SIGNING_EXPECTED_SUBJECT` (stable subject identity, not a rotating
    thumbprint) required by Stage A preflight, `sign-release.ps1` under the
    production gate, and the Stage B gate (CURRENT policy == manifest
    subject == actual certificate subject; "actual == manifest" alone never
    sufficient).
  - Stage B least privilege: JOB 1 `verify` (contents: read; documented
    `actions: write` deviation solely for the same-run verification handoff
    upload; all gates; candidate files handled strictly as DATA — never
    executed) and JOB 2
    `publish` (needs: verify; contents: write; NO candidate execution, NO
    build/sign; final hash identity check + release mutation + asset
    verification).
  - DigiCert action pinned to the full immutable SHA
    `fae23a455ba4bde62b64fd7cb2f81ade788f5a95` (v1.2.1, verified via the
    GitHub API); legacy local-PFX secrets removed from production Stage A;
    client-auth P12 materialized with PowerShell/.NET to a random private
    runner.temp path with an always-run cleanup; signtool verification now
    `verify /pa /v /tw`; RFC3161 timestamper identity recorded in the
    manifest and cross-checked at Stage B; `build.yml` gates
    `scripts/release-tooling-tests.ps1` (exact-SHA hosted-CI gate).
  - `scripts/release-tooling-tests.ps1` extended to 118 deterministic cases
    (old-pre-HSM candidate rejected by current policy, policy-schema
    missing/stale/current, candidate-policy isolation + hostile-tooling
    immunity, publisher policy missing/mismatch/consistent-wrong, Stage A
    dispatch contract, Stage B run-head-SHA binding, no PFX secrets in
    Stage A, DigiCert pin, verify-job permissions, publish-job forbidden
    operations, timestamp missing/warned) — all PASS locally. Docs
    (`publication-gates.md`, `code-signing.md`), decision record, and
    OpenSpec change (section 14) updated.
  - Real production signing remains `BLOCKED_EXTERNAL`; v1.0.0 is PREPARED
    BUT INTENTIONALLY NOT PUBLISHED; verdict stays GO FOR RELEASE CANDIDATE
    / BETA ONLY.
- Campaign G (candidate-execution elimination — final release-control
  hardening, independent review round 4, this session):
  - P0 trust-boundary closure: Stage B now executes ZERO candidate code in
    EITHER job. The verify job's `--version` / `--selftest-native-abi`
    execution step ("Verify downloaded executable identity") is REMOVED;
    publish-release.yml contains no `--version`, no `--selftest`, no
    `Start-Process`, no candidate-script invocation, and no path under
    `candidate-source/` or `candidate-artifact/` in an execution position
    (candidate source and candidate artifact are DATA ONLY: read/parse/hash/
    Authenticode-verify/asset-upload).
  - Identity retained WITHOUT execution: sourceCommitSha == Stage A run head
    SHA == candidate-source checkout SHA; candidate-source
    TabDock.csproj <Version> (parsed as data) == manifest.semanticVersion;
    manifest buildIdentity semantic/informational versions (generated +
    verified by trusted Stage A) == manifest version; on-disk SHA ==
    manifest artifactSha256 == SHA256SUMS == finalSignedSha256; run-id and
    artifact-name bindings; independent Authenticode + RFC3161 verification.
    Native ABI coverage stays outside publication: exact-SHA build.yml,
    windows-2022 native-abi-evidence, Stage A qualification, and the external
    Windows 10/11 gates.
  - P1 checkout credential hardening: `persist-credentials: false` on every
    actions/checkout in publish-release.yml (policy ×2 + candidate-source),
    prepare-release-candidate.yml, release.yml, and build.yml (×2); the
    cross-run download-artifact `github-token` inputs are preserved.
  - `scripts/release-tooling-tests.ps1` extended from 118 to 134
    deterministic cases: the 16 named candidate-execution-elimination and
    checkout-credential tests (execution-position invariant via run-block
    extraction, whole-file absence of candidate flags, data-only artifact
    classification, trusted-record identity, persist-credentials checks,
    final hash/signature gates and publish-job zero-build/zero-sign
    retained) — all PASS locally.
  - Docs updated: publication-gates.md (final trust-model diagram, why
    Stage B does not execute the candidate, checkout credential hardening),
    code-signing.md section 8, decision record addendum, OpenSpec change
    section 15, STATE.md checkpoint.
  - Real production signing remains `BLOCKED_EXTERNAL`; v1.0.0 is PREPARED
    BUT INTENTIONALLY NOT PUBLISHED; verdict stays GO FOR RELEASE CANDIDATE
    / BETA ONLY.
- Campaign H (production hardening campaign, this session):
  - Split navigation is now deterministically gated in hosted CI, not
    only by supervised SendInput: `SplitInteractionPolicy` (pure hit ->
    action classifier) proves a handled preview event still suspends the
    pair for a non-member, while member/button/right-click/hover/stale
    hits are correctly filtered; `SplitPresentationPolicy` has exhaustive
    coverage (3/4 tabs, repeated C/D switching 20 cycles, alternating,
    member focus A<->B without suspension, dormant resume, explicit exit,
    member removal while dormant/presented with survivor promotion,
    stale/recycled identity rejection, RecoveryPending fail-closed,
    generation monotonicity, IsCurrentSettle staleness, dormant
    relationship survival). `TabDock.UnitTests` 136 deterministic cases.
  - Native presentation operation budgets are now regression-tested:
    `PresentationOperationBudget`/`IPresentationBudgetSink` counting seam
    (Hide/PositionAndShow/DeferBatch/SetForeground/LayoutSplit/Single/
    PairZOrder) plus `SplitPresentationController` (pair identity,
    presented/dormant, foreground, generation, settle; wraps policy +
    interaction policy) and `PresentationLayoutCoordinator` (coalesced
    relayout, generations, redundant suppression, ensureFinalPass latch).
    14 budget tests assert A->B, pair->C, C->pair, member focus, and
    coalesced move/resize each as bounded/exact operation counts via a
    fake shepherd/budget sink — no real windows required.
  - ContainerWindow responsibilities materially reduced through safe
    incremental extraction: split state/policy and layout coordination
    are independently testable controllers; `ContainerWindow` retains only
    WPF wiring (WndProc, chrome, timers, hit-testing). Build remains
    `TabDock.sln` with normal `dotnet build`.
  - Deep render/jitter hardening: `RequestRelayout` latches
    `ensureFinalPass` even when pending (WM_EXITSIZEMOVE final z-order pass
    survives queued frame), Render callbacks clear pending BEFORE execute,
    stale settle/layout callbacks generation-gated, dormant pair never
    receives split relayout, `_refusedPaneByHwnd` only cleared on
    constraint change not per frame, `CompositionTarget.Rendering`
    idempotent arm/disarm, zero animation (instant teleportation).
  - Production workflows pinned to immutable full SHAs (actions/checkout
    3d3c42e... v7.0.1, setup-dotnet a98b56... v6.1.0, setup-node 820762...
    v7.1.0, upload-artifact 043fb4... v7.0.0, download-artifact 37930b...
    v7.0.0, digicert fae23a... v1.2.1 preserved); human-readable `# vX`
    comments and `gh api` update procedure documented; `persist-credentials:
    false` preserved; `release-tooling-tests` verifies no mutable tag
    remains.
  - Repository collapsed to main-only: `agent/staging` and the
    `promote-staging` workflow no longer exist. `main` is the sole
    development/integration branch and is qualified directly on push by
    `build.yml` (exact-SHA hosted-CI gates); PRs targeting `main` are also
    qualified. `docs/release/repository-protection.md` documents the
    main-only model and the exact bypass ruleset for `github-actions[bot]`.
    Action pinning, `persist-credentials: false`, and the two-stage
    release chain are preserved.
  - Release external gates are precise and unfakeable: `publication-
    gates.md` gate vocabulary lifecycle (PASS/FAIL/BLOCKED_EXTERNAL/
    BLOCKED_ENVIRONMENT), exact gate table (prerequisite -> command ->
    evidence -> four-way binding), unfakeable operator procedure (download
    exact artifact, `Get-FileHash` == `finalSignedSha256` == `SHA256SUMS`
    == `--version`), stale/future/schema guards; `final-smoke.md`/
    `mixed-dpi-qualification.md`/`compatibility-matrix.md` tightened;
    OpenSpec release-engineering deltas updated; tooling already enforced.
  - Adversarial audit after refactor: fixed recycled-HWND strong gates
    (LayoutSplitPanes/Suspend/RestoreMinimized/NoteGuestMoveSize via
    `IsCurrentCapturedWindow`), timer/WndProc/drag handler leaks
    (`RemoveHook`, null timers, stop close-prompt timer), `GroupViewModel.
    Detach` unsubscribe, `PresentationLayoutCoordinator` stale-Render
    generation gate, `GuestLifecycleService` debounced re-lookup + debounce
    teardown + `IsSuspendingSplitPair` suppression preventing suspension
    misclassified as tray-close; stale `CapturedWindow`/collection mutation/
    MessageBox reentrancy/CompositionTarget leaks dismissed with evidence
    or already mitigated. `HardeningRegressionTests` 6 deterministic cases.
  - Docs reconciled: `docs/ARCHITECTURE.md` (vertical split /
    movement-sync / release chain delegated to controllers, jitter
    hardening notes, promotion note), README qualification counts (137
    release-tooling + hosted-CI split/budget gate note), `publication-
    gates.md` action pinning + gate table.
  - Validation: `dotnet build TabDock.sln -c Release` PASS,
    `TabDock.UnitTests` 136 PASS, `release-tooling-tests.ps1` 139 PASS
    (exact-SHA hosted-CI gate in `build.yml`), `validate.ps1` canonical
    qualification PASS; real production signing/hardware/manual evidence
    remain `BLOCKED_EXTERNAL` — REPOSITORY-SIDE HARDENING COMPLETE,
    EXTERNAL RELEASE EVIDENCE BLOCKED_EXTERNAL.
- Campaign H fix (exact-SHA admission race) — superseded by the main-only
  collapse: the `agent/staging` + `promote-staging` race-free promotion
  architecture was retired. The repository is now main-only: `build.yml`
  qualifies every pushed SHA on `main` (and PRs targeting `main`) directly,
  and there is no `agent/staging` branch or `promote-staging` workflow to
  race. Action pinning, `persist-credentials: false`, PR/fork protection
  (PRs are qualified but never auto-promote), and the two-stage release
  chain are all preserved.
  - `scripts/release-tooling-tests.ps1` retains the main-only invariants:
    `main-only-build-qualifies-main`, `promote-staging-workflow-removed`,
    and `only-publish-release-holds-contents-write` (the release publisher
    remains the only `contents: write` workflow).
  - Docs reconciled: `repository-protection.md` (main-only model),
    `ARCHITECTURE.md` (main-only release chain note), README (main-only
    qualification note).
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
  `scripts/release-tooling-tests.ps1` (137 cases; no real certificates, no
  publishing, no provider contact) — run before every release-pipeline
  change AND gated by hosted CI in `build.yml` (exact-SHA gate).
- Headless unit suite (`TabDock.UnitTests`): 136 deterministic cases
  (SplitPresentationPolicy exhaustive, SplitInteractionPolicy,
  PresentationOperationBudget, HardeningRegression, Geometry, Group,
  Persistence, Converters) — gated in `build.yml` and `validate.ps1`.
- The release chain is `scripts/release-qualify.ps1 -Ci -Sha <sha> -Version
  1.0.0 -Sign` (locally: without `-Ci`); it fails on SHA mismatch, dirty
  trees, published-exe identity mismatch, expected-version disagreement with
  the authoritative csproj `<Version>`, or self-report/hash mismatch, and
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
   mixed-DPI hardware, production signing credentials, Windows 10 x64
   compatibility), then the two-stage production dispatch: configure the
   approved production signer (repository variables/secrets per
   `docs/release/code-signing.md` section 4: `SIGNING_PROVIDER=digicert-stm`,
   `SM_HOST`, `SM_API_KEY`, `SM_CLIENT_CERT_FILE_B64`,
   `SM_CLIENT_CERT_PASSWORD`, `SM_KEYPAIR_ALIAS`, optional
   `SIGNING_EXPECTED_SUBJECT`), dispatch `prepare-release-candidate.yml`
   against the exact final SHA (produces the ONE signed immutable candidate
   with `signingProvider=digicert-stm` / `signingKeyProtection=CLOUD_HSM`),
   download that exact candidate, run the human/mixed-DPI/Windows
   compatibility gates against those exact bytes, author
   `release-external-evidence.json` (schemaVersion 2) with the run id and
   artifact name, then dispatch `publish-release.yml` with that run id and
   evidence; do not publish v1.0.0 without it.
4. Do not create a state-only commit merely to record a SHA or CI run.

## Persistence single-writer fix (this session, deterministic DONE; live A–G PENDING)

- Applied `tabdock_persistence_single_writer_fix.py` (`--check` then `--apply`) to
  `Services/PersistenceService.cs`. The fix establishes a true single-writer gate:
  - one disk writer for state.json / .bak / .tmp — `CommitJson` is the only code
    path that touches those files, serialized by `lock (_writeGate)`.
  - monotonic latest-wins save generations — every `Save`/`SaveAsync` claims the
    next `Interlocked` generation; only the most-recently-attempted generation may
    write, so an older/delayed async snapshot can never clobber a newer save.
  - rapid `SaveAsync` coalescing — a burst collapses to the single newest disk write.
  - synchronous `Save` uses the same serialized gate (no interleave with debounced).
  - ordinary active-tab switching keeps its off-thread path (no synchronous disk I/O).
  - recovery journal / `PendingRecoveryService` untouched (separate, synchronous).
  - Added `public Task WhenWritesSettledAsync()` (graceful-shutdown flush; the
    project's reference assembly strips internals, so it must be public).
- Deterministic validation (all green, local):
  - `dotnet build TabDock.sln -c Debug` and `-c Release`: 0 errors.
  - `dotnet test tests/UnitTests/TabDock.UnitTests.csproj -c Release`: 146 PASS
    (added `PersistenceSingleWriterTests` — off-thread debounce, rapid coalesce
    latest-wins, stale-async protection, concurrent sync+async gate consistency,
    150-snapshot     hammer parseable/no-temp-collision; the concurrency hammer now fires 1000
    SaveAsync snapshots plus periodic synchronous Save barriers).
  - `pwsh -File scripts/validate.ps1 -Configuration Release -Ci -Publish`: OpenSpec
    20/20, publish + `--version` identity (commit 6ebdd8a) PASS.
  - `pwsh -File scripts/release-tooling-tests.ps1`: 139 PASS.
  - `git diff --check`: clean (CRLF normalized to match repo). Both ValidationDriver
    - GuineaPig build Release; `--list` enumerates shards/scenarios.
- NOT DONE (requires a real interactive Windows session, no mouse/keyboard during
  SendInput — cannot be executed by the agent): the supervised live-desktop
  acceptance A–G (four-tab split escape, member focus, rapid switching w/ latency,
  intervening HWND, drag/resize counts, hard-kill recovery, lifecycle torture, and
  the >=1000 SaveAsync + synchronous-barrier persistence concurrency hammer). The
  driver is built and ready; run in an interactive session:
  `dotnet run --project tests\ValidationDriver\TabDock.ValidationDriver\TabDock.ValidationDriver.csproj -c Release -- --yes --configuration Release all`
  Do NOT declare the campaign complete until A–G pass on the real desktop.
