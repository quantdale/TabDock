# Plan: Release-Candidate Completion & Product Hardening

**Status:** complete
**Owner/session:** Codex
**Updated:** 2026-08-23
**Starting branch:** `codex/ship-readiness-overhaul-20260823`

## Objective

Continue PR #12 from its existing ship-readiness head and turn the UI overhaul
into a defensible release-candidate branch. Audit the changed capture and WPF
surfaces, fix verified correctness/accessibility/responsiveness defects, add
deterministic source-contract and interaction coverage, extend supervised
scenario wiring, reconcile documentation and release evidence, and preserve
the Shepherd/no-reparent/native safety architecture.

## Guardrails

- Git is authoritative; preserve the existing branch and working-tree changes.
- Do not rewrite `WindowShepherdService`, recovery ordering, mutation leases,
  persistence transactions, native positioning, or split authority without a
  reproduced defect and a failing regression first.
- Keep `ContentHost`, `DisplayTabs`, `ActiveTab`, capture admission, identity
  validation, and split policy/controller ownership intact.
- Use the canonical OpenSpec CLI for any new behavior specification; do not
  hand-edit generated mirrors.
- Do not claim supervised/native qualification when the desktop is not safe for
  exclusive real-input execution.

## Work waves

- [x] Establish Git/PR state, read repository instructions/checkpoint,
  architecture/testing/audit/release material, and inspect the PR diff.
- [x] Run and record the deterministic baseline; inventory current tests,
  automation contracts, binding warnings, and release workflow gates.
- [x] Wave 1 — capture identity and refresh correctness: verify the picker key
  against canonical native identity, add deterministic HWND/PID/process/path
  continuity cases, and ensure stale/disappeared selections fail closed.
- [x] Wave 2 — picker scale and async hygiene: exercise 100/500/1000 synthetic
  candidates, batch bulk selection notifications where justified, and protect
  filter/icon generations without wall-clock correctness assertions.
- [x] Wave 3 — keyboard/accessibility/source contracts: fix focus, names,
  descriptions, state semantics, and critical XAML contracts while preserving
  ValidationDriver AutomationIds.
- [x] Wave 4 — responsive visual/product interaction pass: address clipping,
  DPI/long-text/minimum-size issues, shared-style inconsistencies, and
  discoverability gaps in launcher/container/picker.
- [x] Wave 5 — launcher/container/split projection regression coverage and
  ValidationDriver scenario updates for the redesigned product.
- [x] Wave 6 — user-facing failures, documentation/OpenSpec reconciliation,
  release candidate evidence, and final gate execution.
- [x] Perform repeated stability checks for touched async/concurrency paths,
  record supervised/environment blockers honestly, update state, and push the
  branch without merging PR #12.

## Evidence ledger

Record each verified defect with symptom, root cause, regression, fix, and
validation command in the final audit/state update. Keep external blockers
separate from deterministic pass/fail results.

## Evidence recorded so far

- Baseline before campaign edits: Debug unit suite 652/652.
- Current Debug unit suite after the picker, projection, accessibility, and
  persistence fixes: 671/671; the targeted source-contract/split regression
  set is 11/11.
- Picker identity coverage includes same-window title/path-case continuity,
  PID/process-start/class replacement, disappearance, filtered selection,
  and duplicate-capture admission. Synthetic scale coverage exercises 100,
  500, and 1,000 rows; icon dispatch is observed in batches rather than by a
  wall-clock budget.
- Verified persistence defect: concurrent `SaveAsync` callers could publish an
  older task as `_lastWriteTask`, allowing `WhenWritesSettledAsync` to return
  before the highest-generation write. The fix tracks the highest generation
  under a small bookkeeping lock; the disk gate/transaction remains unchanged.
  The isolated persistence set passes 5/5 and a subsequent full Debug run
  passes 671/671.
- Supervised real-input qualification is not claimed; this desktop is not
  certified safe for exclusive SendInput execution. Exact rerun commands are
  recorded in `docs/TESTING.md` and the final handoff.
- Release-tooling regression: 150/150. Release qualification before the final
  commit passed audited restore with no vulnerable packages, Release builds,
  671/671 tests, native ABI, version/doctor/recovery/privacy smokes, OpenSpec
  30/30, and self-contained publish/version smoke. The final SHA will rerun
  this gate because embedded build provenance is commit-derived.
- Final deterministic evidence is green: Debug and Release solution
  builds/tests, audited Release validation/publish, native ABI, release
  tooling, strict OpenSpec, 38/38 ValidationDriver self-tests, three repeated
  14/14 CapturePicker runs, and two repeated 671/671 full Debug runs. The
  evidence-only handoff update is followed by one final provenance-qualified
  validation before push.

## Handoff criteria

Debug/Release builds and tests, release tooling, canonical validation,
OpenSpec, native ABI, source-contract/accessibility tests, and `git diff --check`
are green; final branch/remote SHA and worktree state are resolved dynamically;
PR #12 remains open/draft unless explicitly authorized otherwise; supervised
scenarios are listed as executed or blocked with exact rerun commands.
