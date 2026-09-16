# Plan: TabDock release finalization from beta feedback

**Status:** complete for the available desktop — implementation, automated qualification, documentation closure, exact Release physical qualification, targeted ValidationDriver qualification, final-tree artifact smoke, mainline integration, and exact-SHA hosted checks complete; mixed-DPI/Windows 10 physical repeats, the full physical ValidationDriver catalog, and production signing remain external or unperformed qualification items
**Owner:** Codex
**Started:** 2026-09-16

## Objective

Investigate the four beta observations against the current TabDock executable,
fix confirmed product defects at their owning boundaries, add meaningful
regression coverage, harden the affected workflows, and qualify the exact
Release artifact before integrating it into `main`.

## Scope and constraints

- Preserve the Shepherd architecture: captured guests remain independent
  top-level windows and are positioned over the container.
- Preserve unrelated working-tree changes; the pre-existing
  `.codex/config.toml` edit is intentionally not touched until final hygiene
  can establish its ownership.
- Do not add arbitrary timing, application-name exceptions, or foreground
  stealing. Native desktop claims require physical evidence.
- A fail-closed ValidationDriver lease is an environment limitation, not a
  product pass; a later lease-qualified run is recorded as a real product
  result.

## Steps

1. Establish the current Git/build/test/environment baseline and collect fresh
   physical evidence for reports A-D. (Done; see investigation record.)
2. Add source-boundary regressions that fail against the popup/minimize/
   foreground seams, then implement view-owned visual-stack reconciliation for
   every TabDock context-menu opening path plus guarded lifecycle/focus
   transitions. (Done.)
3. Extend the supervised ValidationDriver group-menu scenario to assert native
   point ownership while the menu is open, and add an identity-verified split
   foreground-pairing scenario. (Done; the exact final Release run passed the
   real-input/native inverse z-order assertions.)
4. Run targeted and full automated validation, refresh the Repowise map, and
   reproduce the original popup, overlap, Spotify, switching, and geometry
   workflows on rebuilt Debug/Release candidates. (Done. The exact candidate
   Release artifact was physically exercised, including the reported
   application class and geometry paths. The evidence closure is now complete.)
5. Run the canonical Release/CI validation and publish tooling, then exercise
   the exact published artifact in supervised real-Windows scenarios. Include
   mixed-DPI geometry, z-order matrix, multi-guest stress, persistence, and
   lifecycle checks that the available desktop permits. (Candidate Release
   gate and physical matrix done. The final-tree artifact identity/smoke also
   passed; mixed-DPI remains unavailable on the current desktop.)
6. Reconcile current documentation/state with verified reality, inspect the
   complete diff, commit coherent changes, rebuild the exact resulting Release
   artifact, push `main` without force, and independently verify remote SHA and
   CI status. (Done; the implementation is integrated and the exact-SHA
   hosted checks passed.)

## Evidence and decisions

- Baseline SHA, reproduction details, and current dispositions are recorded in
  `.agent/investigations/beta-feedback-baseline-2026-09-16.md`.
- The first confirmed defect is a local z-order transition around WPF
  `ContextMenu` HWND creation. The active guest is correctly positioned before
  the menu opens, but the container is raised during menu activation and the
  delayed foreground reassert is intentionally suppressed while chrome is
   active. The blank state is therefore a covered guest, not a guest-rendering
   delay. The candidate now repairs the popup-local stack after the popup HWND
   exists and restores ordinary pairing only when TabDock still owns foreground.
- A separate Windows ordering observation showed a deferred split geometry batch
  reporting success while leaving the container between the two guests. The
  candidate uses measured, sequential z-order writes only at foreground-owned
  split boundaries and refuses background repairs. A delayed activation guard
  protects the same inverse invariant.

## Handoff

The campaign is complete for the available desktop. `main` and `origin/main`
are aligned at the verified final tree. Mixed-DPI and Windows 10 physical
repeats, the full physical ValidationDriver catalog, and production
Authenticode signing remain unverified or external qualification items.
