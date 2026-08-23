# Plan: Product Trust & Interaction Campaign

**Status:** implementation and deterministic validation complete; delivery in progress
**Owner/session:** Codex
**Updated:** 2026-08-23

## Objective

Close the queued product gaps on current `main`: expose pending-recovery
attention in the launcher, surface canonical capture-admission state in the
launcher and containers, add focus-independent scoped tab navigation, and add
an always-visible canonical split affordance. Preserve the Shepherd
no-reparent architecture, recovery fail-closed/supervised boundaries, split
identity/presentation semantics, and durable mutation rules.

## Scope and constraints

- Baseline is resolved dynamically: `main` at `9488bf1`, matching
  `origin/main`, clean after `git fetch origin`.
- Use `GroupManager` as the canonical capture-admission authority; expose its
  state rather than duplicating rules. Shortcut availability remains a
  separate diagnostic state.
- Use `PendingRecoveryService` discovery for a read-only pending count; the
  launcher must never mutate recovery evidence or bypass supervised commands.
- Route local and global tab navigation through one container-level operation
  backed by `TabNavigationPolicy`; global hotkeys are `Ctrl+Alt+PageUp` and
  `Ctrl+Alt+PageDown` with `MOD_NOREPEAT`, and only act for proven current
  TabDock guest/container foreground HWNDs.
- Route split affordance actions through the existing
  `SplitPresentationController`, `SplitPresentationPolicy`, and current
  container mutation/presentation paths. Do not add a split state machine or
  raw `Tabs`/`DisplayTabs` index conversion in view code.
- Do not hand-edit generated OpenSpec mirrors; archive/sync with the canonical
  OpenSpec CLI workflow.

## Steps

- [x] Establish baseline: read AGENTS/state/architecture/testing/product
  basics, fetch remote, resolve Git state, inspect active OpenSpec changes.
- [x] Create this durable campaign plan and link it from `.agent/STATE.md`.
- [x] Complete OpenSpec proposal, design, delta specs, and implementation
  tasks for recovery visibility, admission presentation, global navigation,
  and discoverable split control.
- [x] Wave A: add pure seams and deterministic tests for pending-count
  projection, admission transitions, hotkey scoping/navigation, and split
  affordance state/action projection.
- [x] Wave B: implement service/view-model/container/launcher integration,
  including UI Automation names/IDs and live event updates.
- [x] Wave C: perform the cross-feature integration audit and add focused
  regression/accessibility/authority tests.
- [x] Wave D: update architecture, testing, README/help, OpenSpec, and state.
- [x] Validate Debug/Release builds and tests, release tooling, CI/publish
  validation, OpenSpec, diff check, and repeated full-suite stability.
- [x] Run supervised ValidationDriver scenarios only if the desktop and
  provenance guards are safe; otherwise record exact blockers/commands.
- [x] Archive the completed OpenSpec change; commit coherent campaign changes,
  push `main`, and verify `main == origin/main` and a clean worktree as the
  final delivery action.

## Evidence and decisions

- Product Basics deliberately deferred these four items; see
  `.agent/plans/product-basics-2026-08-23.md`.
- Current source already contains `TabNavigationPolicy`, hardened split
  controller/policy paths, pending recovery discovery/execution, and
  `GroupManager.SetCaptureAllowed`; the campaign should add presentation seams
  around them rather than replace them.
- `Ctrl+Alt+Left/Right` is rejected because the mission identifies common
  graphics/display-driver collisions; use PageUp/PageDown instead.
- OpenSpec change archived at
  `openspec/changes/archive/2026-08-23-product-trust-interaction-campaign/`;
  canonical specs are synced under `openspec/specs/`.
- Deterministic gate: Debug and Release builds have 0 warnings/0 errors;
  Debug and Release suites each pass 649/649; release-tooling passes 150/150;
  `validate.ps1 -Ci -Publish` passes its 649/649, native ABI, redirected
  lifecycle/privacy, OpenSpec 29/29, and Release publish checks; diff check is
  clean.
- Stability evidence: the existing full-suite abandoned-mutex fact recurred
  in both configurations. A test-only explicit owner-exit barrier plus
  `GC.KeepAlive` repaired the ordering; ten complete Debug and ten complete
  Release repetitions then passed 648/648. A separate Release icon-generation
  test exposed and received an explicit test-only completion gate. See the two
  stability investigations under `.agent/investigations/`.
- Supervised scenarios are environment-blocked because the desktop cannot be
  certified exclusively available for real-input ValidationDriver execution;
  exact commands are documented in `docs/TESTING.md` and the OpenSpec task
  record.

## Handoff

**Next action:** commit the intended campaign work, push `main`, and verify
remote synchronization and a clean tree.
**Blockers:** supervised desktop qualification remains
`BLOCKED_SUPERVISED`/`BLOCKED_ENVIRONMENT` until an exclusively available,
provenance-safe Windows desktop is provided; deterministic validation is green.
