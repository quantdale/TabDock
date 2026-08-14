# Plan: Persistent split relationship and rendering qualification

**Status:** active
**Owner/session:** Codex
**Updated:** 2026-08-14

## Objective

Keep a runtime split relationship defined across ordinary non-member selection;
present a selected non-member full-width while the pair is dormant; restore the
same LEFT/RIGHT pair when either member is selected; suppress redundant member
context-menu actions; and qualify client presentation with controlled windows
and available Chromium-family browsers.

## Scope and constraints

- Preserve Shepherd/no-reparent, journal-before-hide, identity/recovery guards,
  input safety, and runtime-only split membership.
- Separate relationship existence from current presentation mode everywhere that
  affects visibility, geometry, z-order, constraints, diagnostics, and queued
  settle callbacks.
- Do not synthesize WM_SIZE, add browser-specific hacks, or change persistence.
- Keep baseline comparison evidence outside the repository.

## State model

- `SplitRelationshipDefined`: `_splitLeft` and `_splitRight` are valid captured
  members and the composite projection remains installed.
- `SplitPairPresented`: the relationship is currently the visible two-pane set;
  `_shepherdActiveWindow` and `_splitForeground` are pair members.
- `SingleGuest`: the relationship remains defined but the selected non-member is
  the only visible guest; the last focused pair member remains remembered.
- Explicit exit clears the relationship. Structural member invalidation clears
  it and preserves the existing survivor promotion contract when presented, or
  preserves the current non-member guest when dormant.

## Steps

- [x] Inventory all split callers and classify relationship versus presentation.
- [x] Add focused follow-up OpenSpec and reconcile the canonical spec.
- [x] Implement centralized suspend/resume/exit state transitions and guarded
      presentation relayout.
- [x] Update diagnostics and paired-member context menus.
- [x] Update ValidationDriver scenarios and add rendering/client evidence seams.
- [x] Build and run deterministic validation; attempt guarded interactive/browser
      qualification where the environment permits.
- [ ] Compare baseline and candidate, audit the diff, commit/push only after
      applicable gates, and verify exact-SHA hosted CI.

## Evidence and decisions

- Baseline `HEAD` and `origin/main` are both `8b75c99cdd149648b54f98ed2ff0f9f2598bd0fc`.
- Existing `ContainerWindow.SplitInteractionFix.cs` calls `ExitSplit(target)`
  for a third-tab click, so the rejected behavior is explicit and localized.
- `GroupViewModel.DisplayTabs` already suppresses the RIGHT member while a
  composite exists; retaining the composite is sufficient for dormant display.
- `CreateDiagnosticSnapshot` currently uses one `SplitActive` flag both for
  relationship metadata and expected visible panes; it must distinguish these.

## Handoff

**Next action:** inspect the final diff, commit the candidate, rerun the
canonical Release/publish gate for the candidate SHA, push `main`, and verify
the exact-SHA GitHub Actions run.
**Qualification blocker:** the guarded ValidationDriver capture preflight
refused the current desktop because the discovered TabDock root HWND was
outside its verified test identity scope; the Computer Use native pipe was
also unavailable. No input-safety guard was bypassed. Browser qualification is
therefore external/manual evidence only in this environment.
