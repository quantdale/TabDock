# Plan: Mainline Long-Run Reliability and Native Resource Lifecycle Hardening

**Status:** complete — implementation, qualification, and delivery preparation
**Owner/session:** Codex
**Updated:** 2026-08-24
**Target branch:** `main`
**Starting integrated source:** `df89d15467ea854a685dc0417c3708e32b183497`

## Objective

Extend the already-qualified TabDock validation architecture with measurable,
deterministic resource-lifecycle evidence for sustained capture, split, picker,
WinEvent, persistence, diagnostics, and restart churn. Fix only concrete
ownership defects reproduced by the new measurements. Leave the complete
campaign on `main`, with a bounded headless regression gate, an opt-in safe
extended soak, machine-readable evidence, honest external-gate classifications,
and an exact pushed SHA.

## Mainline prerequisite

- The qualified release-closure head was fast-forwarded onto `main` from the
  common integrated mainline without rewriting history.
- Local `main` and `origin/main` both resolved to `df89d154...` after the push.
- All further implementation is directly on `main`; no topic branch is the
  integration authority.

## Workstreams

1. Re-baseline the integrated source with the documented Debug/Release builds,
   unit suites, deterministic ValidationDriver self-tests, release-tooling
   tests, strict OpenSpec, and canonical CI-safe validation.
2. Audit `IDisposable`, native handles, icons, timers, cancellation, process
   wrappers, hooks, hotkeys, HwndSource hooks, event subscriptions, streams,
   temporary artifacts, caches, and shutdown paths. Record owner/lifetime gaps
   without mechanically refactoring process-lifetime resources.
3. Add a test-side immutable resource snapshot model and Windows measurement
   provider for process identity, handles, USER/GDI objects, private bytes,
   working set, threads, and safe top-level-window observations.
4. Add a deterministic analyzer for warm-up, plateau, bounded noise, transient
   spikes, persistent/late monotonic growth, invalid/missing samples, and
   generation changes; cover it with data-driven unit tests.
5. Add reusable resource-series artifact writing and a bounded resource-aware
   validation path. Keep cleanup ownership strict and avoid physical input or
   arbitrary desktop interaction in CI.
6. Exercise complementary lifecycle profiles over existing pure seams and,
   where safe/test-owned, real process/UI lifecycle: group/capture, split,
   layout/minimize, picker/icon refresh, WinEvent monitor, diagnostics,
   persistence/recovery, and restart residue.
7. Integrate a short headless gate, an explicit extended safe soak command, and
   a machine-readable resource-stability artifact bound to source/run identity.
8. Add/update the OpenSpec change and documentation only through the normal
   workflow; preserve functional, release, privacy, and supervised-input
   boundaries.
9. Repeat analyzer, bounded churn, picker/icon, monitor, and representative
   end-to-end safe runs; complete the full validation ladder; commit meaningful
   checkpoints and push exact `main`.

## Completion record

- Mainline normalization: the qualified release-closure head was fast-forwarded
  from `df89d154...` onto `main`; subsequent checkpoints were committed and
  pushed directly to `origin/main`.
- Ownership audit: complete; production lifetimes had explicit cleanup at the
  inspected boundaries and no concrete production resource leak was reproduced.
- New validation coverage: immutable snapshots/analyzer, Windows probe,
  eight lifecycle profiles, source-bound JSON/JUnit evidence, safe CLI, and CI
  gate are implemented.
- Counts: 707/707 Debug and Release unit tests; 143/143 Release deterministic
  self-tests; 16/16 resource self-tests; 177/177 release-tooling tests; strict
  OpenSpec 35/35.
- Stability: five consecutive 128-cycle headless runs, a 256-cycle run, a
  1,000-cycle Release headless soak, and a 64-sample run-owned Release process
  soak passed. The Release process sample showed handles 956→954, USER 27→24,
  GDI 19→18, threads 13→13, windows 9→9, private bytes −901,120, and working
  set +544,768, all within the documented budgets.
- OpenSpec change `resource-lifecycle-qualification` was archived into the
  canonical `openspec/specs/resource-lifecycle-qualification/spec.md` after
  implementation and validation.

## Constraints and decisions

- Shepherd remains the sole native presentation authority; measurement never
  mutates windows or installs a second repair loop.
- GroupManager and SplitPresentationController remain the state authorities.
- Resource evidence is separate from physical, mixed-DPI, signing, and human
  smoke qualification. Missing or unavailable measurements fail closed rather
  than becoming PASS.
- No guarded `SendInput` or blind desktop automation will run without a proven
  supervised lease. Autonomous work uses pure seams or run-owned test windows.
- Thresholds must have deterministic tests, rationale, and bounded headroom;
  USER/GDI/handle budgets receive stricter treatment than noisy memory signals.
- Generated artifacts remain ignored/local and no credentials, user paths,
  titles, URLs, command lines, or document content enter retained evidence.

## Validation ladder

- Focused unit tests after each component, including repeated analyzer runs.
- Debug/Release solution builds and unit suites.
- ValidationDriver deterministic self-test suites, including new resource
  analyzer/lifecycle tests.
- Bounded headless resource regression gate and opt-in safe extended soak.
- Release-tooling tests, strict OpenSpec validation, `git diff --check`, and
  `scripts/validate.ps1 -Configuration Release -Ci -Publish`.
- Final exact SHA, clean worktree, and `HEAD == origin/main` proof.

## External blockers retained honestly

Supervised physical input, `dragreorder` H2 flip-back, `split-drag-release`
zero-delta, `capture-inline-ui` second-tab, mixed-DPI hardware, Windows 10 and
independent Windows 11 evidence, approved production signing, and final human
smoke remain blocked unless real qualifying evidence is obtained. Synthetic or
headless resource PASS cannot satisfy any of those gates.

## Checkpoint protocol

Update `.agent/STATE.md` at baseline, after the ownership audit, after each
resource component/profile, after any reproduced defect, after meaningful
validation, before commit/push, and at final handoff. Record durable facts and
exact counts/SHA values, not a command transcript.
