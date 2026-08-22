# Tasks

## 1. Regression net (red)

- [ ] 1.1 Add `PersistenceBackupDurabilityTests` with injected-seam fault tests:
      staging-read failure, backup-candidate flush failure, backup install
      failure, primary temp write failure, primary install failure — each
      asserting previous `.bak` intact where applicable, primary untouched, no
      `_lastSavedJson` advancement, and a successful retry afterwards.
- [ ] 1.2 Cover: valid `.bak` survives failed next-backup creation; missing
      primary keeps existing `.bak`; stale `.tmp`/`.bak.tmp` never authoritative;
      unchanged-save optimization and latest-wins generation still hold.
- [ ] 1.3 Add pending-recovery regression matrix: resolution liveness bounding,
      full-retirement sidecar removal, crash-after-source-deletion convergence,
      partial-source retention, unreadable-sidecar retention, non-retired
      transaction retention, fresh-generation non-inheritance, legacy fallback
      preservation.
- [ ] 1.4 Re-anchor the tests that pinned the old orphan-sidecar behavior on
      the new lifecycle (`ResolvedEntryRetirement_CanBeRetriedToCompletion`,
      `UnreadablePendingSibling_DoesNotBlockOtherDiskOnlyCleanup`,
      same-generation replay, legacy replay).

## 2. Implementation (green)

- [ ] 2.1 PersistenceService: staged durable backup candidate + atomic install
      before primary replacement; skip stage when primary missing; extend the
      internal test constructor seam; keep single-writer gate, generation gate,
      unchanged-save optimization, and load-side classification untouched.
- [ ] 2.2 PendingRecoveryService: `CompactUnreachableResolutions` invoked from
      both ledger rewrite paths; sidecar deletion in RetireEntry after source
      deletion; `SweepOrphanedResolutionSidecars` in RunInteractive only.

## 3. Specification

- [ ] 3.1 persistence-resilience delta: ADDED requirement for the staged backup
      transaction with failure-window scenarios.
- [ ] 3.2 hidden-window-journal delta: ADDED requirement for bounded resolution
      bookkeeping and orphaned sidecar retirement with convergence scenarios.
- [ ] 3.3 `openspec validate --all --no-interactive` passes.

## 4. Qualification

- [ ] 4.1 Debug + Release builds 0 warnings / 0 errors.
- [ ] 4.2 xUnit suite green in Debug and Release; report exact counts.
- [ ] 4.3 `scripts/release-tooling-tests.ps1` green; `scripts/validate.ps1
      -Configuration Release -Ci -Publish` PASS.
- [ ] 4.4 `git diff --check` clean; STATE.md handoff updated with next queued
      campaign recommendation.
