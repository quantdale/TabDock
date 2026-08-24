# TabDock agent state

## Git authority

Git is authoritative for `HEAD`, branch, `origin/main`, and worktree state.
Resolve them dynamically at every fresh session. This file never records a
self-referential SHA or treats an old CI run as evidence for the commit that
contains this text.

## CURRENT CAMPAIGN — MAINLINE LONG-RUN RESOURCE LIFECYCLE HARDENING

**Objective:** instrument and qualify sustained TabDock lifecycle churn for
native/UI/process resource stability, fix concrete ownership defects found by
measurement, and deliver the complete result on `main` with a bounded
headless gate and opt-in safe extended soak.

**Plan:** `.agent/plans/resource-lifecycle-hardening-2026-08-24.md`

**Status:** active; Wave 0 mainline normalization, integrated baseline, source
ownership audit, resource model/analyzer, lifecycle profiles, artifact writer,
headless gate, CI wiring, OpenSpec planning, and documentation are complete.
Final Release validation, repeated extended evidence, commit/push, and archive
remain.

### Current phase

- Wave 0: complete.
- Baseline revalidation: complete after integration; Debug/Release builds and
  unit suites were green, release tooling was 177/177, canonical Debug CI
  validation passed, and OpenSpec was 35/35.
- Ownership audit: complete in
  `.agent/investigations/resource-ownership-audit-2026-08-24.md`; no concrete
  production resource leak has been reproduced or asserted.
- Measurement/analyzer: implemented with Windows driver-side snapshots,
  strict native-count/byte budgets, fail-closed unavailable/error/generation
  handling, and 19 unit tests plus 16 deterministic self-tests.
- Lifecycle profiles/evidence: implemented for eight complementary profiles;
  headless 128-cycle repeats and a 256-cycle run passed, and a run-owned
  process sample passed with flat observed counters.
- CI/docs/OpenSpec: bounded `validate.ps1 -Ci` gate, retained workflow
  evidence, documentation, and strict-valid change
  `resource-lifecycle-qualification` are in the worktree.

### Mainline checkpoint

- Session start `origin/main` was
  `c2c480707f9e9fdfa753b4885f53bca270c775bc`.
- The common base of main and the campaign stack was
  `ba3115a138eed81e4a56c023aa3381f2c14a20cd`.
- The normal history-preserving merge checkpoint was `7ed8769` (main plus
  `origin/codex/qualification-control-plane-20260824`); resolve the current
  `HEAD` dynamically after the reconciliation commit.
- The campaign stack remains historically traceable through
  ship-readiness `eca8670...`, native determinism `09a204d...`, and
  qualification control `b0101a3...`. No historical campaign branch was
  rebased, force-pushed, rewritten, or merged into `main`.
- The qualified head `df89d15467ea854a685dc0417c3708e32b183497` was
  fast-forwarded onto local `main`; immediately after normalization local
  `main` and `origin/main` were identical at that SHA. All further work is
  directly on `main`; current uncommitted campaign edits must be committed and
  pushed before handoff.

### Completed reconciliation

- `main` is documented as the sole integration/release authority. Temporary
  topic/development/draft-review branches are allowed as finite provenance and
  review surfaces; no permanent staging hierarchy or promotion workflow is
  allowed.
- Canonical specs now contain the final hardening, native-interaction,
  qualification-control-plane, topology, and release-evidence behavior.
  Completed changes were archived under
  `openspec/changes/archive/2026-08-24-*`; `openspec list` has no active
  changes.
- Strict OpenSpec validation is green at 34/34 canonical specs after archive.

### Prior integration and evidence checkpoint

- The integration branch was pushed without rewriting any historical campaign
  branch. Its remote head matches the local final head; resolve both
  dynamically with Git.
- The prior fresh qualification-only candidate was bound to the then-final
  committed source SHA, semantic version 1.0.0, release manifest,
  ValidationDriver, primary run manifest, qualification bundle schema 1, and
  portable package schema 1. It is invalidated as final evidence by the
  foreground-transition harness fix. A new candidate must be generated from
  the final committed tree before handoff.
- Candidate bundle verification, portable-package verification, deterministic
  returned bundle verification, and data-only returned-report import all
  passed with zero failures. The returned evidence is synthetic-deterministic
  and does not establish physical qualification.
- No candidate binary, returned executable, or returned script was executed by
  the report importer; imported evidence was hash-verified as data only.
- The final qualification-only artifact, bundle, portable package, deterministic
  returned bundle, and data-only report import all bind the same final source
  identity and candidate bytes; their exact paths and hashes are in the session
  handoff. They remain qualification-only and unsigned.

### Deterministic validation at the final source checkpoint

- Debug/Release solution builds: 0 warnings / 0 errors.
- Debug/Release unit suites: 686 / 686.
- ValidationDriver Debug/Release deterministic self-tests: 127 / 127 after
  the foreground-transition lease regressions.
- Release-tooling regression suite: 177 / 177.
- Canonical `scripts/validate.ps1 -Configuration Release -Ci -Publish`: PASS,
  including audited restore, native ABI, diagnostics/recovery/privacy,
  strict OpenSpec 34/34, single-file publish, and version smoke carrying the
  committed foreground-transition fix identity.
- No integration conflict or evidence-backed Critical/High regression was
  found. No guarded physical `SendInput` was issued.

### External gates and known classifications

- Physical qualification remains `BLOCKED_SUPERVISED` /
  `BLOCKED_ENVIRONMENT`: this host has no proven exclusive supervised desktop
  lease. The three known repeats (`dragreorder` H2 flip-back,
  `split-drag-release` zero-delta polyline, and `capture-inline-ui` second-tab
  assertion) have an explicit safe-session classification of
  `BLOCKED_SUPERVISED`/`BLOCKED_ENVIRONMENT`; their scenario-specific
  product-versus-harness verdicts remain pending authoritative exact-candidate
  runs. Synthetic replay does not close them.
- Mixed-DPI hardware, real Windows 10 x64 evidence, independent Windows 11
  evidence, approved production signing credentials, and final human smoke
  remain blocked external gates. Synthetic topology remains synthetic-only.
- GitHub CLI remains unauthenticated, but the existing PR #12 was updated by
  an ordinary fast-forward push; public PR metadata confirms it is open/draft,
  mergeable, targets `main`, and hosted `build` plus `native-abi-evidence`
  checks passed for the pushed head. This campaign is now authorized to develop
  directly on `main`; do not publish or weaken release-integrity gates.

### Validation and evidence rules

- Resource evidence must be immutable, privacy-safe, source/run-bound, and
  machine-readable. Missing or inaccessible measurements never become PASS.
- Synthetic/headless resource PASS is resource-only and cannot satisfy
  supervised physical input, mixed-DPI, Windows-version, signing, or human
  smoke gates.
- No guarded `SendInput` or blind desktop automation is authorized without a
  proven exclusive supervised lease. Autonomous runs use pure seams or
  test-owned processes/windows and clean up in `finally`.
- Keep generated artifacts, logs, caches, machine paths, credentials, and
  secrets out of Git.

### Current campaign measurements

- Headless synthetic resource gate: 32-cycle CI profile PASS; five consecutive
  128-cycle runs PASS; 256-cycle all-profile run PASS.
- Run-owned Debug TabDock process sample: 16 samples, synthetic=false,
  PASS. Handles 956→956, USER 24→24, GDI 18→18, threads 13→13, top-level
  windows 9→9, private bytes 68,186,112→68,186,112, working set
  124,657,664→124,657,664; all trends Flat.
- No actual production leak was found. No production ownership fix was
  justified; the only cleanup correction was in the validation runner's
  process-wrapper handling.

### Known external blockers retained

Supervised physical repetitions, `dragreorder` H2 flip-back,
`split-drag-release` zero-delta, `capture-inline-ui` second-tab, mixed-DPI
hardware, real Windows 10 x64, independent Windows 11, approved production
signing, and final human smoke remain blocked unless real evidence is
obtained. Do not convert these gates to synthetic PASS.

### Next action

Run the final Debug/Release ladder, Release resource gate and safe extended
soak repeatedly; update task/state records, archive the completed OpenSpec
change, commit meaningful checkpoints on `main`, push `origin/main`, and prove
identical SHAs with a clean worktree. Keep all supervised, hardware, OS,
signing, and human-smoke gates honestly blocked.
