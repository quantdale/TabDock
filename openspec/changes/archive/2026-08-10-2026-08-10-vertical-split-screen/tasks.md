# Tasks — vertical split screen

## Phase 1 — Core split state and geometry
- [x] Add split state fields to `ContainerWindow` (`_splitLeft`, `_splitRight`, `_splitForeground`) and `IsSplitActive`/`IsSplitMember` helpers, keyed by `CapturedWindow` reference.
- [x] Add `SplitRect` (50/50 physical-pixel split) and `SplitPaneRect`.
- [x] Add `WindowShepherdService.PositionGuest`, `SetForeground`, and explicit
  chrome/container z-order primitives.

## Phase 2 — Enter/exit split
- [x] Add `EnterSplit(left, right)` (transition from prior pair journal-safely; hide any prior non-pair visible guest).
- [x] Add `ExitSplit(keepActive)` (return to single visible guest; hide departing member journal-safely; never release).
- [x] Add `LayoutSplitPanes` (position both panes; pair the container below the
  partner; per-pane 1px redundant-glue guard).

## Phase 3 — Context-menu workflow
- [x] Add `ConfigureSplitMenuItems` (disabled at <2 tabs; direct action at 2; submenu at 3+; exit item when split active) + `StartSplitFrom` + three click handlers.

## Phase 4 — Lifecycle integration
- [x] Split-aware `SyncShepherdActiveWindow` (click member keeps split; click third tab exits).
- [x] Split-aware `StateChanged` (minimize hides both; restore lays out both) and `RelayoutGuests` (LocationChanged/SizeChanged/LayoutUpdated).
- [x] Split-aware `WndProc` WM_ACTIVATE reassert (re-lay both + foreground the active member).
- [x] Split-aware `PairZOrderBehindGuest` (re-pin container behind the other member on a split-member foreground).
- [x] Split-aware `RestoreMinimizedWindow` and `NoteGuestMoveSize` (either member; drag-out measured against the member's own pane).
- [x] `GuestLifecycleService.OnWindowHidden` gate relaxed for split members (a split member hiding itself is teardown).
- [x] `HandleSplitMemberRemoved` via `Tabs_CollectionChanged` (only `Remove` actions; reorder `Move` must not tear down the split).

## Phase 5 — ValidationDriver scenarios
- [x] Add `Scenarios.Split.cs` with `IsInPane`, `ClickTabSubmenuItem`, `EnterSplitTwo`, `AssertSplitPanes` and the split scenarios, including direct input, context-menu stability, X/middle-click pop-out, and native move/size re-glue coverage.
- [x] Register the 15 scenarios in `AllOrder` + runner switch; add `Uia.IsMenuItemEnabled`.
- [x] Run the complete scenario set in clean-session batches below the
  ValidationDriver time budget; targeted runs already pass the new direct-input,
  context-menu, native re-glue, X, middle-click, and inline-capture scenarios.

## Phase 6 — UI/z-order stabilization
- [x] Route guest positioning and container pairing through deterministic
  `WindowShepherdService` primitives; remove global `HWND_BOTTOM` repair.
- [x] Separate popup/chrome ordering from logical guest visibility and reconcile
  once after WPF popup close.
- [x] Replace native title-bar drag-out with deterministic pane re-glue.
- [x] Add tab X and middle-click Pop out semantics without sending `WM_CLOSE`.
- [x] Move routine Add App capture into the existing container visual tree.
- [x] Complete the three-application manual torture run and clean-session
  regression batches.

## Phase 7 — Documentation / spec sync
- [x] Update `docs/internal/split-screen-implementation-waypoint.md`.
- [x] Create OpenSpec change `2026-08-10-vertical-split-screen` (proposal, delta spec, tasks).
- [x] Update `docs/ARCHITECTURE.md` and `docs/TESTING.md` with split-aware notes and the new scenario list.
- [x] Run the sanctioned OpenSpec sync/archive workflow once the change is complete.

## Phase 8 — Regression hardening
- [x] Run the relevant existing regression scenarios (spec §37) in a clean session.
- [x] Independent swarm reviews (Shepherd invariants, state correctness, UI regression, test gaps); reconcile findings.
- [x] Final `git diff` review; confirm no reparenting/style mutation and no debug/scaffolding remains.
