# Native Interaction Determinism, Replay & Reliability Campaign (2026-08-24)

**Status:** complete for this environment; stacked PR creation needs GitHub auth
**Owner/session:** Codex
**Branch:** `codex/native-interaction-determinism-20260824`
**Base:** PR #12 head `eca8670759f9bc42aee58ec5f59b33fd0adab3f0`

## Objective

Make difficult Windows interaction failures classifiable and replayable while
preserving the Shepherd/no-reparent safety architecture. Extend the existing
ValidationDriver contracts rather than creating a second result or ownership
system. Move all native-free policy that can be proven without a desktop into
deterministic fixtures, and keep physical qualification conservative when the
desktop lease cannot prove exclusive safety.

## Non-negotiable invariants

- External guests remain independent top-level windows; no reparenting, style
  surgery, owner mutation, or `AttachThreadInput` architecture.
- `WindowShepherdService`, `SplitPresentationController`, `GroupManager`,
  `DisplayTabs`, and `ProductMutationLease` retain their authority.
- Identity is proven at the current HWND generation before mutation or input;
  adopted external windows are bounded input targets and never cleanup-owned.
- Journal-before-mutation and fail-closed `RecoveryPending` behavior remain.
- Evidence is privacy-safe, bounded, deterministic, and never claims physical
  qualification from an invalid environment.

## Baseline (2026-08-24, before campaign edits)

- `HEAD`: `eca8670759f9bc42aee58ec5f59b33fd0adab3f0`
- `origin/main`: `ba3115a138eed81e4a56c023aa3381f2c14a20cd`
- PR #12: draft, head is the same qualified branch, base `main`.
- Worktree: clean.
- Debug build: 0 warnings / 0 errors.
- Debug unit tests: 675/675.
- Release build: 0 warnings / 0 errors.
- Release unit tests: 675/675.
- ValidationDriver deterministic self-tests: 38/38.
- Release tooling tests: 150/150.
- Strict OpenSpec validation: 30/30.
- Canonical `scripts/validate.ps1 -Configuration Release -Ci -Publish`:
  PASS, including native ABI, diagnostics/recovery/privacy smokes, publish,
  and version smoke.
- `git diff --check`: clean.
- Physical qualification: not run; current desktop is not certified as an
  exclusive supervised SendInput environment.

## Historical findings disposition

Before changing a historical area, verify current source and record one of
`STILL PRESENT`, `ALREADY FIXED`, `SUPERSEDED`, `EXTERNAL ONLY`, or
`NEEDS REPRODUCTION`. The specifically requested staged backup, recovery
compaction, orphan sidecar cleanup, intentional-hide finalization,
diagnostic-suppression cleanup, split liveness, dormant drag projection,
direct GroupViewModel mutation coverage, and real layout dirty-check items are
already represented as completed campaigns in the current checkpoint; verify
their source/tests and do not redo them.

## Work waves

- [x] Establish Git/PR state, branch from the exact PR #12 head, read project
  instructions/evidence/OpenSpec guidance, and run the deterministic baseline.
- [x] Audit current ValidationDriver result, wait, input, discovery, cleanup,
  provenance, evidence, and scenario seams; audit WinEvent/lifecycle/identity/
  foreground production boundaries.
- [x] Add the canonical qualification outcome contract and native-free tests.
- [x] Add capability discovery and a conservative desktop qualification lease
  with deterministic state-machine tests.
- [x] Consolidate scenario-scoped ownership/provenance semantics and tests.
- [x] Add bounded native interaction timeline and root run-manifest evidence.
- [x] Extract the smallest replay seams and add deterministic fixtures for
  WinEvent/lifecycle/identity/foreground/split/containment decisions.
- [x] Decompose duplicated ValidationDriver waits/input/evidence/cleanup and
  classify the three physical repeats without best-of-N pass semantics.
- [x] Measure WinEvent duplicate work; optimize only if counts demonstrate
  meaningful redundant work and behavioral equivalence is pinned.
- [x] Add deterministic stress/model suites with fixed seeds.
- [x] Update docs/OpenSpec and commit coherent implementation waves.
- [x] Run all final deterministic gates, push the stacked branch, and leave PR
  #12 draft/unmerged.

The final branch push succeeded. Creating a draft PR stacked on PR #12 was
attempted but GitHub CLI has no authenticated session in this environment;
that external action remains for an authenticated maintainer. PR #12 was not
modified or merged.

## Checkpoint commits

- `cc83c86` — qualification contracts, capabilities, lease, provenance,
  evidence, waits, replay seam, fixtures, and deterministic suites.
- `172f042` — WinEvent routing instrumentation and admission/dispatch metrics.

Current checkpoint before the final gate: driver self-tests 96/96, focused
WinEvent/replay tests 13/13, strict OpenSpec 31/31, and Release driver/build
checks green. No physical run was performed because the desktop was not
demonstrably exclusive and safe for SendInput.

## Evidence ledger

Each discovered defect is recorded as `REPRO -> ROOT CAUSE -> RED REGRESSION
-> FIX -> FOCUSED GREEN -> FULL GREEN`, separately for product and harness.
Blocked physical work records the exact blocker and does not become PASS.

## Handoff criteria

Debug/Release builds and tests, driver self-tests, release tooling, canonical
validation/publish, strict OpenSpec, native ABI, privacy checks, replay/model/
stress suites, and `git diff --check` are green. The final handoff reports
exact deterministic counts, per-scenario physical classifications, branch/PR
state, artifacts, commits, and external blockers.
