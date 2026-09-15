# Plan: fix initial-capture black presentation and consolidate repository

**Status:** active
**Owner/session:** Codex
**Updated:** 2026-09-15

## Objective

Independently establish the initial-capture black-screen root cause, implement
and validate the smallest correct production fix with regression coverage, then
integrate all intended work into `main` and remove obsolete branch/PR scaffolding.

## Scope and constraints

- Preserve the Shepherd/no-reparent architecture and native identity/recovery gates.
- Treat existing black-screen branches and PRs as evidence only until independently reviewed.
- Preserve or classify every unique branch, PR, stash, and worktree artifact before cleanup.
- Do not use unattended synthetic input; distinguish executable validation from unavailable physical/hosted gates.

## Steps

- [x] Capture repository, branch, PR, worktree, stash, and reachability inventory.
- [x] Reconstruct capture, WPF, native presentation, single, and split lifecycles.
- [ ] Falsify lifecycle, dirty-check, z-order, overlay, and native-transaction hypotheses.
- [ ] Add a behavior-level regression that fails on current `main`.
- [ ] Implement and validate the production fix.
- [ ] Review every preserved branch/PR/stash and classify unique work.
- [ ] Validate the integrated `main`, push it, and clean obsolete refs/PRs.
- [ ] Verify final main-only repository invariants and update `STATE.md`.

## Evidence and decisions

- Preservation inventory: `.agent/investigations/initial-capture-black-screen-2026-09-15.md`.
- A disposable WPF/HwndHost probe showed `Loaded` before `Show()` returns but
  `ContentRendered` later; a guest/container z-order pair established before
  the first render was not preserved at the `ContentRendered` observation.
- Current `ContainerWindow` has no `ContentRendered` reconciliation; its
  `LayoutUpdated` handler suppresses unchanged content rectangles.

## Handoff

**Next action:** finish the disposable lifecycle probe and add the red regression seam.
**Blockers:** physical real-input visual qualification is unavailable in the current app surface; use safe headless/integration evidence and report the capability boundary.
