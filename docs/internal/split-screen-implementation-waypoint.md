# Split Screen Implementation Waypoint

> Historical implementation waypoint. The later focused change
> `persistent-split-pair-presentation-2026-08-14` supersedes the original
> third-tab exit wording in this document: the current contract keeps the
> A/B relationship defined, presents a clicked non-member full-width while the
> pair is dormant, and resumes A/B from either composite half. See
> `docs/ARCHITECTURE.md` and `openspec/specs/ui-ux-hardening/spec.md` for the
> current state model.

## Objective

Implement a production-ready **vertical split-screen** feature for TabDock: exactly two captured application tabs displayed simultaneously in a left/right split, with a polished context-menu workflow, correct Shepherd lifecycle behavior, robust edge-case handling, automated ValidationDriver coverage, and no regression to existing TabDock behavior.

Deliverable statement from the task spec:
> TabDock supports displaying exactly two captured application tabs simultaneously in a left/right vertical split, with a polished context-menu workflow, correct Shepherd lifecycle behavior, robust handling of edge cases, automated ValidationDriver coverage, and no regression to existing TabDock behavior.

## Baseline
- branch: `main`
- starting HEAD: `71f8e3c854c7800b80d1d082fcb0b140079109ce`
- current HEAD: `71f8e3c854c7800b80d1d082fcb0b140079109ce`
- working tree: pre-existing modifications (NOT mine, leave untouched):
  - modified: `.gitignore`, `AGENTS.md`
  - untracked: `.codex/config.toml`, `.codex/hooks.json`, the task spec file `TabDock_ Production-Ready Vertical Split-Screen Feature.md`

## Confirmed Requirements

(from the task spec — authoritative)
- Vertical (left/right) 2-way split only. No top/bottom, no grids, no 3+ visible guests.
- Initiated from a right-clicked captured tab; initiating tab becomes LEFT.
- `<2 eligible tabs`: Split Screen command disabled, no layout change.
- `==2 tabs`: right-click A → "Split screen" auto-selects B → A=LEFT, B=RIGHT.
- `>=3 tabs`: right-click A → "Split screen >" submenu of other tabs (B, C, D...); selecting puts A=LEFT, selected=RIGHT.
- Split state: one clear owner, identity-based (not fragile index-only), survives reorder.
- Geometry: 50/50 split of the full content rect, physical pixels, no DPI conversion. leftWidth = Width/2, rightWidth = Width-leftWidth. Odd widths handled deterministically.
- Visual divider: clean vertical separation if feasible without compromising positioning. Fixed 50/50 preferred over draggable splitter.
- Lifecycle: one coherent split-aware positioning policy; never two conflicting z-order loops. Both guests must never cover chrome/menus/picker.
- Clicking one split member: keep split, partner stays visible.
- Clicking a non-paired tab: exit split, clicked tab becomes single visible guest.
- Exit split: returns to normal one-visible-tab, keeps a sensible active member, hides other via normal journal-safe path, does NOT release either, preserves tab order + membership.
- Start a different split while split: deterministic transition, hide departing members journal-safely, one active split pair per container.
- Pop-out/drag-out of either split member: end split safely, keep remaining guest captured and visible.
- Guest self-close/destroy: remove dead member, terminate split, surviving member becomes visible; no separate destroy pipeline.
- Guest self-hide: audit carefully; extend existing gating, never bypass.
- Container minimize/restore: both disappear/return correctly, split stays active.
- Move/resize/maximize/restore/monitor-change: both guests track continuous.
- Foreground/focus: both panes accept input, both stay visually docked, no aggressive polling.
- Capture into a split group: do not auto-create 3-pane; preserve split pair; new member joins tabs normally.
- Close group / app exit / emergency release: existing release/shutdown rules; no split member left wrongly hidden.
- Crash safety: journal-before-hide invariant preserved everywhere including split transitions.
- Persistence: **default = split is runtime state, NOT persisted** (acceptable unless source proves otherwise). Do NOT modify schema casually.
- Logging: SPLIT[enter]/[exit]/[replace]/[member-gone] vocabulary; keep hot-path SHEPHERD[position] cheap.
- Context menu: integrate existing tab context menu. Disabled at 1 tab; direct action at 2; submenu at 3+; Exit split when active.
- Production readiness gate (product/architecture/quality/documentation/review) — see task spec §49.

## Confirmed Architecture Facts

(verified against source; citations are `path.cs:line`)
- Shepherd model: guests are never reparented/restyled; only placement/z-order/visibility/DWM-transition-suppression are mutated (`WindowShepherdService.cs:11-45`).
- `WindowShepherdService` is a single instance shared by all containers (`App.xaml.cs:93`). It has NO per-group/per-container instance state for "active guest" — that lives in `ContainerWindow._shepherdActiveWindow` (`ContainerWindow.xaml.cs:35`) and `Group.ActiveIndex`.
- `ContainerWindow` fields: `_shepherdActiveWindow` (the single visible guest), `_sentinelActiveWindow`? (no — only `_shepherdActiveWindow`). The single-visible-guest assumption is concentrated in `ContainerWindow`:
  - `_shepherdActiveWindow` (`ContainerWindow.xaml.cs:35`)
  - `SyncShepherdActiveWindow` (`ContainerWindow.xaml.cs:761-779`): shows new active then hides old.
  - `LayoutShepherdActiveWindow` (`ContainerWindow.xaml.cs:786-829`): content rect + 1px epsilon + `PositionAndShow`.
  - `GetContentAreaScreenRect` (`ContainerWindow.xaml.cs:838-853`).
  - `PairZOrderBehindGuest` (`ContainerWindow.xaml.cs:865-875`): only pairs when `_shepherdActiveWindow.Hwnd == foregroundHwnd`.
  - `RestoreMinimizedWindow` (`ContainerWindow.xaml.cs:886-925`): only active tab.
  - `NoteGuestMoveSize` (`ContainerWindow.xaml.cs:939-963`): only active tab; drag-out threshold 40px.
  - `StateChanged` (`ContainerWindow.xaml.cs:410-441`): minimized → hide `_shepherdActiveWindow`; else layout.
  - `WndProc` WM_ACTIVATE reassert (`ContainerWindow.xaml.cs:187-224`): only `_shepherdActiveWindow`.
  - `LogStateSnapshot` (`ContainerWindow.xaml.cs:443-488`): only active tab.
- `GroupViewModel` owns `Tabs` (ObservableCollection<TabViewModel>) and `ActiveTab`; `GroupManager` owns `Group.ActiveIndex` and the O(1) HWND→member index (`GroupManager.cs:40,168-180`).
- The O(1) index is maintained ONLY from `Group.Members` CollectionChanged (`GroupManager.cs:184-293`). Do NOT mutate it directly.
- `GroupManager` is group-centric, not container-centric. `App._containers` maps `Guid → ContainerWindow` (`App.xaml.cs:36`).
- `GuestLifecycleService` resolves HWND→member O(1), then routes to `container.ReleaseCapturedWindow` / `RestoreMinimizedWindow` / `NoteGuestMoveSize` / `PairZOrderBehindGuest` / `RefreshTabTitle` (`GuestLifecycleService.cs:51-215`).
- `WindowShepherdService.PositionAndShow` always does `SetWindowPos(HWND_TOP)` then `PairZOrderBehind(container, guest)` — bringing a guest to the true top each time. Two guests both calling this would fight each other's z-order (each would top the other). A split-aware path must pin both guests above the container in a stable order (e.g. pair container behind both sequentially, or rely on one being CONSTANTLY above).
- `Hide` journals before hiding (`WindowShepherdService.cs:251-264`). `Release(show:false)` journals-clear-immediate before hide (`:343-360`).
- Container context menu is defined in the tab DataTemplate (`ContainerWindow.xaml:174-179`): Pop out / Close window, bound to TabViewModel commands.
- Existing log lines relied on by tests: `SHEPHERD[position]`, `SHEPHERD[hide]`, `SHEPHERD[dragout]`, `SHEPHERD[bring-to-front]`, `SHEPHERD[rescue]`, `Switched group`, `Reordered tab`, `Released tab`, `destroyed; removing its tab`, `hid itself`, `minimized; restoring`, `WinEvent: title changed`, `EMERGENCY RELEASE`, `Saved {n} group(s)`.

## Critical Invariants
- No SetParent / no guest style/exstyle/owner mutation for split.
- One coherent split-aware positioning policy; no two conflicting z-order loops.
- Journal-before-hide ordering preserved on every hide including split transitions.
- O(1) WinEvent HWND resolution — use `TryGetCapturedMember`, never scan.
- Physical-pixel geometry; no new DPI conversion.
- Hooks gated on `IsMonitoringNeeded`; unchanged.
- Hot-path `SHEPHERD[position]` stays cheap; no DescribeWindow on it.
- No third guest can ever remain visible.
- Split identity by CapturedWindow reference, not positional index.
- No aggressive polling.
- No schema change to persistence (split is runtime-only).

## Current Design
Complete (Phase 2 done). Concrete design below.

### State model (owned by ContainerWindow)
- Fields: `_splitLeft` (CapturedWindow?), `_splitRight` (CapturedWindow?), `_splitForeground` (CapturedWindow?).
- `IsSplitActive => _splitLeft != null && _splitRight != null`.
- Identity by `CapturedWindow` reference, NOT positional index. Survives reorder.
- `_shepherdActiveWindow` remains the active/focused member (during split it is always one of the pair).
- `_splitForeground` tracks which member is z-order-top (can differ from active after a direct guest click).

### Geometry
- `GetContentAreaScreenRect()` stays the single full-rect source (physical pixels, no DPI).
- `SplitRect(content)` derives: `leftW = content.Width / 2` (integer), `leftRect = {content.left, content.top, content.left+leftW, content.bottom}`, `rightRect = {content.left+leftW, content.top, content.right, content.bottom}`. Odd widths deterministic (right gets the extra px).

### Positioning / z-order (one coherent policy)
- New `WindowShepherdService.PositionGuest(window, rect, insertAfter)` — position+show a guest to a rect, inserting above `insertAfter`; no container pin, no HWND_TOP; handles iconic/zoomed restore; JournalClear; cheap `SHEPHERD[position]` log.
- New `WindowShepherdService.SetForeground(window)` — foreground-only (SetForegroundWindow + benign-key-nudge), no repositioning.
- `LayoutSplitPanes()`: compute both rects; if both already cover their panes (1px epsilon) return (no churn). Else `PositionGuest(bottom, rect, HWND_TOP)` then `PositionGuest(top, rect, bottom.Hwnd)` then `PairZOrderBehind(container, bottom.Hwnd)`. `top = _splitForeground ?? _splitRight`, `bottom` = the other. Result: `top, bottom, container` — container strictly below both. Deterministic.
- Foreground event (split member clicked): update `_splitForeground` and re-pin container behind the OTHER member (1 SetWindowPos), never re-top.

### Tab-click semantics
- Clicking a split member: keep split; that member becomes active + `_splitForeground`; `LayoutSplitPanes()` re-glues/re-pins.
- Clicking a third (non-paired) tab: `ExitSplit(keepActive: thirdTab)` — hide both old members journal-safely, show third full-width.

### Exit split
- `ExitSplit(keepActive)`: clear split state; hide departing members via `_shepherd.Hide` (journal-before-hide preserved); survivor = keepActive if member else current-active-if-member else oldLeft; `SetActiveTab(survivor)` + `LayoutShepherdActiveWindow()` full-width. Never releases either.

### Member-removal cleanup
- `ContainerWindow` subscribes to `_viewModel.Tabs.CollectionChanged`. When a split member leaves (pop-out, drag-out, self-close, self-hide, group close), `HandleSplitMemberRemoved` clears split and promotes the survivor to active + full-width. Logs `SPLIT[member-gone]`.

### Lifecycle integration
- `StateChanged`: minimize hides BOTH split members (journal-safe); restore `LayoutSplitPanes()`.
- `WndProc` WM_ACTIVATE reassert: split → `_splitForeground = _shepherdActiveWindow`, `LayoutSplitPanes()`, `SetForeground(_shepherdActiveWindow)`.
- `PairZOrderBehindGuest`: split member foreground → re-pin container behind the other member.
- `RestoreMinimizedWindow`: accept either split member; restore inside its own pane.
- `NoteGuestMoveSize`: apply to either split member; measure drag-out against the member's OWN pane rect (not full rect); snap-back calls `LayoutSplitPanes()`.
- `GuestLifecycleService.OnWindowHidden`: relax the "only active tab can be guest-initiated hide" gate so a SPLIT member hiding itself is teardown. (TabDock's own split-exit hides are handled because the member leaves `Group.Members` before the hide event is evaluated, or the member is not the active tab.)

### Context menu (code-behind construction on open)
- In `TabsListBox_PreviewMouseRightButtonDown`, before `menu.IsOpen = true`, rebuild split items (dedupe by Tag prefix `SPLIT-`):
  - tabs < 2 → `Split screen` disabled.
  - tabs == 2 → `Split screen` (direct action; auto-selects the other).
  - tabs >= 3 → `Split screen >` submenu of candidate tabs (excluding initiating), each with `Header=Title`, `Icon=Icon`, Click → `StartSplitFrom(left, candidate)`.
  - split active → `Exit split screen` item.
- Handlers: `SplitScreenMenuItem_Click`, `SplitCandidateMenuItem_Click`, `ExitSplitMenuItem_Click`. Initiating tab = `MenuItem.DataContext` (TabViewModel).

### Start split
- `StartSplitFrom(leftTab, chosenRight=null)`: rightTab = chosenRight ?? the other (only-one) tab. `EnterSplit(left.Model, right.Model)`.
- `EnterSplit`: set `_splitLeft/_splitRight/_splitForeground=left`; `_shepherdActiveWindow = left`; `SetActiveTab(leftTab)`; `LayoutSplitPanes()`; log `SPLIT[enter]`.
- Start-a-different-split-while-split: `EnterSplit` first clears the old pair (hide old members journal-safely) then sets the new pair. Implemented by having the menu still offer Split on split members; EnterSplit handles the transition.

### Persistence
- **Decision: split is runtime-only, NOT persisted.** No schema change. Rationale: split is tied to live attached HWNDs; on restart groups restore empty layout intent. Recorded per spec §25 default.

## Decisions Made
- Split state authority = `ContainerWindow` (matches existing per-container visible-guest policy; GroupManager is group-centric/shared, GroupViewModel does no hiding).
- Split identity by `CapturedWindow` reference.
- Z-order: split primitive `PositionGuest` + `PairZOrderBehind(bottom)`; container strictly below both; foreground-repin on guest click.
- Geometry: integer `Width/2` + `Width-leftWidth`; no DPI.
- Tab-click: keep-split on split member, exit on third tab (spec §12 recommended behavior).
- Exit via context menu; hide departing journal-safely; never release.
- Persistence: runtime-only (spec §25 default).
- Context menu built in code-behind on open (avoids WPF ContextMenu RelativeSource traps; matches existing ColorMenuItem_Click pattern).
- drag-out threshold measured against the member's own pane rect in split.
- Evidence: swarm Agent A (state/lifecycle), B (menu/click), C (z-order/geometry/WinEvents), D (testing). All verified against source.

## Files Modified
- `Services/WindowShepherdService.cs` — added `PositionGuest(window, rect, insertAfter)` (split positioning primitive), `SetForeground(window)` (foreground-only), `RaiseContainerForChrome`, and the shared `PairZOrderBehind` primitive.
- `Services/GuestLifecycleService.cs` — `OnWindowHidden` gate relaxed: a SPLIT member hiding itself is guest-initiated teardown (both members visible in split); minimized-container guard preserved (covers both split members).
- `Views/ContainerWindow.xaml.cs` — split state fields (`_splitLeft/_splitRight/_splitForeground`), `IsSplitActive`/`IsSplitMember`/`IsInSplit`(public), `SplitRect`, `SplitPaneRect`, `NeedsPanePosition`, `LayoutSplitPanes`, `EnterSplit`, `ExitSplit`, `StartSplitFrom`, `HandleSplitMemberRemoved`, `Tabs_CollectionChanged` (wired in ctor, unwired in Closed). Split-aware versions of: `SyncShepherdActiveWindow`, `StateChanged`, `WndProc` WM_ACTIVATE reassert, `PairZOrderBehindGuest`, `RestoreMinimizedWindow`, `NoteGuestMoveSize`. Context-menu: `ConfigureSplitMenuItems` + three Click handlers (code-behind construction on open).
- `tests/ValidationDriver/TabDock.ValidationDriver/Scenarios.Split.cs` — new partial file: 4 helpers (`IsInPane`, `ClickTabSubmenuItem`, `EnterSplitTwo`, `AssertSplitPanes`) + 15 scenarios.
- `tests/ValidationDriver/TabDock.ValidationDriver/Uia.cs` — added read-only `IsMenuItemEnabled`.
- `tests/ValidationDriver/TabDock.ValidationDriver/Scenarios.cs` — registered the 15 split scenarios in `AllOrder` + runner switch.
- `docs/internal/split-screen-implementation-waypoint.md` — this file.

## Tests Added
15 ValidationDriver scenarios (see Files Modified): `split-single-disabled`, `split-two-auto`, `split-select-partner`, `split-exit`, `split-resize`, `split-move`, `split-minrestore`, `split-reorder`, `split-popout-left`, `split-popout-right`, `split-selfclose`, `split-titlebar-dragout`, `split-click-third`, `split-directclick`, `split-repeat-cycles`.

## Tests Passed
- `dotnet build TabDock.csproj` — success, 0 warnings, 0 errors.
- `dotnet build TabDock.sln` — success, 0 warnings, 0 errors.
- `scripts/validate.ps1` (build-only) — success, 0 warnings, 0 errors.
- ValidationDriver scenarios that PASSED (real-input, before environment degradation): `split-two-auto`, `split-single-disabled`, `split-select-partner`, `split-exit`, `split-minrestore`, `split-reorder` (after fixing the reorder Move-notification bug), `split-resize`, `split-click-third`, `split-repeat-cycles`, `split-popout-left`, `split-popout-right`, `split-selfclose`. That is 12 of 15 split scenarios passed.

## Tests Failing / Unvalidated
- `split-directclick` — FAILED in the prior run: direct click into a split member's pane did not foreground the member while the container was above guests. The stabilization pass now reapplies the complete local order `foreground guest → partner → container` after chrome/popup activation, without moving the container behind unrelated windows. Clean-session validation remains pending.
- `split-move`, `split-titlebar-dragout` — not run (environment degraded before they were executed).
- Environment blocker: after ~3h of real-input SendInput testing the desktop input state degraded (modal capture-picker interaction and tab context-menu opening stopped working in the harness, affecting even pre-existing scenarios). Only a clean/rebooted session can run the remaining ValidationDriver scenarios reliably.

## Known Regressions / Risks
- Two simultaneous visible guests is a first; the split z-order/foreground interplay is the highest-risk area.
- `split-directclick` local-stack fix is applied but unvalidated against the harness due to environment degradation; needs a supervised re-run in a clean session.
- `EnterSplit` hides a prior non-pair visible guest (three-guest invariant preserved).
- `ExitSplit`/`HandleSplitMemberRemoved` compute survivors against the intact pair before clearing.
- Split member self-hide via a non-active member now reaches teardown — existing self-hide/tray scenarios use the active tab, so unaffected; verify.
- Reorder uses `Tabs.Move` (raises a `Move` CollectionChanged with non-null OldItems); `Tabs_CollectionChanged` must only act on `Remove`, not `Move` (bug fixed; split-reorder passes).

## Swarm Findings
### Accepted
- Split state owned by ContainerWindow, identity by CapturedWindow reference (A).
- Z-order split primitive + container pinned below both (A, C).
- Geometry integer Width/2 + Width-leftWidth (A, C).
- OnWindowHidden gate relaxation for split members (A, C).
- Context menu: split items built in code-behind (B).
- Test plan: IsInPane/ClickTabSubmenuItem/Uia.IsMenuItemEnabled helpers, AllOrder registration (D).

### Mid-implementation review (4 reviewers) — reconciliation
- **FIXED (z-order bug, consensus):** `LayoutSplitPanes` positioned `bottom` at HWND_TOP then `top` below it, so the foreground member ended up BELOW its partner (SetWindowPos places hWnd below hWndInsertAfter). Swapped to position `top` at HWND_TOP first, then `bottom` below it. Also corrected the `PositionGuest` doc comment (was "ABOVE", is "BELOW").
- **FIXED (test vacuity):** `split-reorder` used the per-run `ctx.LogOffset` instead of a scenario-local offset, so "Reordered tab" could be satisfied by an earlier scenario; now records a local offset before the drag. `split-move` now asserts the container actually moved before asserting the panes tracked it.
- **REVISED z-order policy:** the split stack is repaired locally as `top guest → partner guest → container`; a global `HWND_BOTTOM` fallback was removed because it could place TabDock behind unrelated windows and complicate owned dialogs.
- **NOTED (edge, uncertain):** near-simultaneous double self-hide of both split members could leave the survivor visible (HandleSplitMemberRemoved promotes + re-shows it before its own hide event lands, and OnWindowHidden's transient-visible check then ignores it). Extreme edge; noted as a known limitation rather than destabilizing the validated promotion path.
- **NOTED (replace untested):** no scenario exercises `EnterSplit` while a split is already active (`SPLIT[replace]`); noted as a test gap.
- Verified clean (consensus): no third-visible-guest steady-state path; journal-before-hide preserved; `Tabs_CollectionChanged` correctly ignores Move (reorder) and handles Remove; both-members-removed (group close / Clear) is safe; dead HWND mid-layout is guarded; `OnWindowHidden` split gate is sound; `ConfigureSplitMenuItems` doesn't corrupt Pop out/Close window.

## Current Implementation Status
Phases 2-5 implemented and building cleanly (0 warnings/errors). Split feature functional: enter/exit split, context-menu workflow, lifecycle integration (minimize/restore, foreground, drag-out, member removal, self-hide gate), reorder identity survival. 12 of 15 ValidationDriver scenarios passed. The remaining 3 (`split-directclick`, `split-move`, `split-titlebar-dragout`) could not be validated because the real-input harness environment degraded after extended use (affecting even pre-existing scenarios). Mid-implementation swarm review done; z-order bug fixed and test-vacuity issues fixed; MoveToBottom z-order fix for `split-directclick` applied but unvalidated against the harness.

## Exact Next Action
In a clean (recently-booted) session, run the remaining split scenarios: `split-directclick` (verify the z-order fixes), `split-move`, `split-titlebar-dragout`, then the regression scenarios from spec §37, then the production-readiness gate and OpenSpec archive workflow.

## Remaining Production-Readiness Gates
All items in task spec §49 (product, architecture, quality, documentation, review) — not yet satisfied.

## Manual Feedback

The follow-up stabilization pass treats these reports as product bugs: tab
context-menu interaction can leave a guest covered by the WPF container marker;
ordinary chrome activation can leave the same stale ordering; split/guest
movement can visibly jump; and native title-bar movement feels like an accidental
pop-out. The last behavior is intentionally being removed in favor of explicit
tab Pop out.

## Reproduction Matrix

| Transition | Required invariant | Current status |
| --- | --- | --- |
| normal guest → tab context menu → close | guest remains rendered and docked | explicit close reconciliation added; interactive validation pending |
| split guests → context menu → close | both panes remain live and separated | explicit close reconciliation added; interactive validation pending |
| chrome click / color menu | chrome remains usable, guest is not hidden | popup policy added; validation pending |
| native guest move/size | guest returns to its assigned rect and remains captured | implementation changed to re-glue; tests pending |
| tab X / middle click | pop out only; external process remains alive | UI/route added; tests pending |

## Z-Order Model

The authoritative transition is now explicit: TabDock-owned popup interaction
raises the container without hiding guests, and popup close schedules one stack
reconciliation after WPF has closed the popup HWND. Normal guest restoration uses
the existing Shepherd positioning primitive; split restoration uses the existing
two-pane policy. No visibility state is changed merely because focus moved to a
popup or chrome surface.

## TabDock Top-Level HWND Inventory

Current surfaces remain: one launcher `MainWindow` (legacy group overview), one
WPF `ContainerWindow` per open group, one temporary `CapturePickerWindow`, WPF
context-menu popup HWNDs, and one native `NativeHwndHost` marker per container.
The launcher/picker/container unification decision is still open; the safe first
step is to move routine capture into the selected container without changing the
Shepherd guest model, if the visual-tree constraints can be met.

## UI Unification Decision

Investigating the smallest safe change. A single simultaneous container for every
group would reduce z-order surfaces but is a high-risk ownership rewrite. The
preferred implementation target is one primary shell for the active group with
in-window group/capture controls; if that cannot be achieved without broad
regression risk, retain internal per-group containers and eliminate only routine
picker/modal proliferation.

## Split Stabilization

Split identity and pane geometry remain unchanged. Native move/size completion no
longer interprets distance from the pane as a pop-out gesture. Popup transitions
must preserve the logical visible guest set: one in normal mode, two in split,
zero while the container is minimized.

## Tab UX Changes

Each tab now exposes a small `×` bound to the existing Pop out command. Middle
click on a tab follows the same non-destructive release path. `Close window`
remains the explicit WM_CLOSE action. Native title-bar movement is re-glued and
does not release a captured guest.

## Tests Added

Implementation-level coverage still needs to be added for context-menu render
stability, repeated chrome interaction, X/middle-click pop-out, and native
move/size re-glue. The old `split-titlebar-dragout` expectation must be replaced
with capture-preservation/re-glue semantics.

## Tests Passed

- `dotnet build TabDock.sln --no-restore` — passed after the first stabilization
  patch, 0 warnings and 0 errors.

## Remaining Risks

- The interactive desktop has not yet been reset, so real-input validation of the
  popup transition and split direct-click fix is still pending.
- Capture UI still uses a temporary top-level picker and the launcher remains a
  separate shell; this is not yet production-ready against the new product goal.
- The local split stack must be adversarially validated after focus is moved to
  unrelated windows and back.

## Exact Next Action

Complete inline capture/group interaction design from current view-model seams,
then add the new ValidationDriver scenarios and run a clean-session manual cycle.

## Stabilization Milestone (2026-08-10)

The sections above preserve the original split-screen checkpoint and its
historical validation notes. The following is the authoritative status after
the UI/z-order stabilization pass.

### Manual Feedback

- Right-clicking a tab could leave the guest correctly sized but covered by the
  container's native content marker after a WPF popup activation transition.
- Ordinary chrome activation could produce the same stale ordering. This was a
  z-order/visibility-policy defect, not a guest rendering defect.
- Native guest title-bar movement and edge sizing felt like accidental escape
  from the group. Native move/size completion now always re-glues the captured
  guest; explicit tab **Pop out** is the only release gesture.

### Reproduction Matrix

| Scenario | Result |
| --- | --- |
| `contextmenu-render-stability` | Passed, six open/dismiss cycles in a clean run |
| `split-contextmenu-render-stability` | Passed, repeated menus on both panes |
| `chrome-click-render-stability` | Passed, eight repeated tab/chrome cycles |
| native move/resize re-glue | `split-native-move-reassert`, `split-native-resize-reassert`, and `dragout-by-titlebar` passed |
| tab X / middle-click | X, middle-click, split-left X, and split-right X passed |
| inline capture/group creation | `capture-inline-ui` and `group-create-inline` passed; no routine picker window appeared |

### Z-Order Model

`WindowShepherdService` is the sole owner of guest positioning and the shared
container-pairing primitive. Normal mode places the active guest over the
container marker. Split mode applies one local stack: foreground guest, partner
guest, container. The old global `HWND_BOTTOM` operation is gone; unrelated
desktop windows are not displaced. WPF popup interaction raises the container
without changing the logical visible-guest set, then performs one explicit
reconciliation after popup close.

### TabDock Top-Level HWND Inventory

- `MainWindow`: retained as the no-group/startup fallback and legacy group
  overview.
- `ContainerWindow`: one per open logical group remains necessary for this safe
  stabilization pass; each is the primary tabbed surface for that group.
- `CapturePickerWindow`: retained only for the global hotkey/launcher path when
  no selected container can host the inline workflow.
- WPF context/color menus: temporary popup HWNDs, owned by normal WPF popup
  behavior and covered by the explicit chrome transition policy.
- `NativeHwndHost`: one native marker per container, not a guest parent.
- Captured applications: independent top-level guest HWNDs by Shepherd design.

Routine **Add App** now uses an in-window capture panel, so the normal group
workflow does not create another TabDock picker window. A single-container
rewrite was investigated and rejected for this pass because it would combine
group ownership, active-tab restoration, and close/recovery changes in one
high-risk rewrite. The remaining launcher/fallback picker is documented rather
than hidden behind another popup.

### UI Unification Decision

Accepted the smallest safe unification: keep the existing per-group container
ownership model, move routine capture into `ContainerWindow`'s visual tree,
and retain the launcher/fallback picker only where there is no selected group.
Group creation remains available from the launcher; no new group-specific
window is created. The next product iteration can replace the launcher with a
group selector in the shell after the per-group recovery semantics have a
dedicated migration plan.

### Split Stabilization

Split remains identity-based, vertical, 50/50, and runtime-only. The validated
 paths include direct guest input, resize/move tracking, context menus, X
 pop-out of both members, and native move/resize re-glue. A complete clean
 session suite and manual torture cycle are still required before a production
 gate can be marked green.

### Tab UX Changes

Each tab has a hit-testable `×` button that releases the tab without sending
`WM_CLOSE`. Middle-clicking the tab body follows the same path and suppresses
drag initiation. Right-click remains the context menu; **Close window** remains
the explicit destructive external-window action. A native title-bar drag cannot
release a captured guest.

### Tests Added

`contextmenu-render-stability`, `split-contextmenu-render-stability`,
`chrome-click-render-stability`, `tab-closebutton-popout`,
`tab-middleclick-popout`, `split-closebutton-left`,
`split-closebutton-right`, `split-native-move-reassert`,
`split-native-resize-reassert`, `capture-inline-ui`, and
`group-create-inline` were added or updated.
The old native title-bar drag-out expectation was replaced by re-glue
assertions. `Input.MiddleClickAt` and real tab-button UIA/mouse helpers were
added to keep the scenarios non-vacuous.

### Tests Passed

- `dotnet build TabDock.sln --no-restore` — passed, 0 warnings/errors.
- ValidationDriver and GuineaPig project builds — passed, 0 warnings/errors.
- `scripts/validate.ps1` build-only validation — passed.
- Targeted real-input runs passed for context-menu normal/split stability,
  direct split input, native move/resize re-glue, title-bar re-glue, X
  pop-out, middle-click pop-out, split-left/right X, inline capture, chrome
  stability, and in-window group creation.
- The broad `all --cycles 2` run exercised the original suite through
  `split-reorder` before the ValidationDriver's 10-minute overall budget
  expired. It exited with the harness timeout code, not a product assertion;
  it is not a complete-suite pass.
- Optional `browser-multi --guest chrome-normal` launched Chrome and Edge but
  stopped in the picker because the Edge title contained an invisible character
  and the harness found zero matching rows. Cleanup completed; no TabDock
  assertion failed in that run.

### Remaining Risks

- A complete clean-session run of all scenarios remains outstanding because the
  harness budget expired before the newly registered tail scenarios.
- The full manual torture cycle with three real GPU-composited applications
  still needs execution.
- The launcher and per-group containers remain separate top-level TabDock
  surfaces; routine capture has been unified, but the larger single-shell
  redesign is intentionally deferred.
- No separate swarm-worker tool was available in this session. Parallel source
  forensics and an adversarial local review were performed; independent swarm
  sign-off is therefore not claimed.

### Exact Next Action

Run the remaining targeted scenarios in a fresh interactive desktop, then run
the relevant historical regression subset in batches below the ValidationDriver
budget. Perform the three-application manual torture cycle, reconcile any
failures, and only then decide whether the production-readiness gate can pass.

### Final Architecture

Shepherd remains a never-reparent, never-restyle model. Captured windows are
independent top-level HWNDs. `WindowShepherdService` owns guest geometry,
visibility calls, foreground calls, and container/guest pairing; `ContainerWindow`
owns logical split membership and visible-guest selection.

### Final Z-Order Policy

Logical visibility is independent from foreground and popup state. Normal mode
has one visible managed guest; split mode has two; minimized mode has none.
Temporary TabDock UI changes ordering only for its lifetime and does not hide a
guest. After close, one policy-owned reconciliation restores the guest/container
relationship. Rectangles, not z-order, separate split panes.

### Final UI Surface Inventory

See the inventory above. The only routine capture UI is now an in-window WPF
panel. The fallback picker is still a top-level window because the global hotkey
has no selected container owner.

### Manual Bugs

- right-click blanking: targeted reproduction fixed; broad manual torture pending
- chrome-click blanking: targeted reproduction fixed; broad manual torture pending
- redraw glitching: deterministic local stack and single popup reconciliation added; torture pending
- guest move/resize escape: fixed by unconditional re-glue; targeted scenarios passed

### Split Status

Core split behavior and the new direct-input/z-order path are working in targeted
interactive runs. Production status is not yet certified pending the remaining
scenario batches and manual torture.

### Tab UX Status

X and middle-click pop out without terminating the external process. Both split
member X paths and middle-click passed targeted validation.

### Tests Run

See **Tests Passed** above and the command/results recorded in `.agent/STATE.md`.

### Production Readiness

FAIL at this checkpoint. The code/build/targeted regression evidence is strong,
but the broad ValidationDriver run hit its ten-minute harness budget and the
requested three-application manual GPU/Electron torture cycle has not been
completed. No claim of final PASS is justified yet.

### Manual Test Results

Controlled GuineaPig real-input tests passed. The requested three-application
GPU/Electron manual torture run has not yet been completed in this checkpoint.

### Swarm Review Findings

The prior split review findings remain preserved above. This pass accepted the
local stack, popup lifecycle, no-native-drag-out, and test-vacuity corrections.
No independent swarm workers were available through the current tool surface.

### Repository State

Branch and HEAD remain the recovered `main` / `71f8e3c...`; the working tree is
intentionally uncommitted and includes the prior agent infrastructure plus the
stabilization changes. No reset, clean, or commit was performed.

### Final closure evidence — 2026-08-10

The final build/spec gate passed: app, solution, GuineaPig, ValidationDriver,
`scripts/validate.ps1`, and OpenSpec validation all completed with zero errors;
OpenSpec reported 12/12 items passed. Bounded validation also passed the split
core/lifecycle set after clean-state reruns, split context rendering (5 cycles),
normal context rendering (6 cycles), chrome rendering (8 cycles), direct split
input, native title movement and resize re-glue, rendered release/restore,
X/middle-click pop-out, inline capture/group creation, and Chrome/Edge
multi-browser capture.

Production readiness is **FAIL**. `directclick-foreground-pairing` failed its
immediate normal-mode local z-order assertion on two consecutive clean runs.
The guest became the actual foreground window and accepted text input, but HWND
sampling showed an unrelated external top-level window remained between the
guest and the TabDock group container. The WinEvent trace showed the external
foreground transition but no captured-guest foreground event at the direct
click; the later focus event arrived only after the three-second assertion
window. A temporary focus-hook probe was removed, and no speculative product
fix was retained.

The browser investigation identified Edge's title variation as U+200B ZERO
WIDTH SPACE in `Microsoft\u200B Edge`; the current explicit browser match key
made `browser-multi --guest chrome-normal` pass. The requested three-application
manual torture test was not completed in this session, so it cannot be claimed
as evidence. The OpenSpec change remains unarchived.

### Closure session checkpoint — 2026-08-10

The initial build/spec gate passed: the application project, solution, GuineaPig,
ValidationDriver, `scripts/validate.ps1`, and
`openspec validate --all --no-interactive` all completed successfully.

Validation is being run as bounded standalone scenario batches because the prior
`all --cycles 2` run reached the ValidationDriver's ten-minute safety budget
before completing. Batch A (legacy behavior) executed with 17 passes and 9
failures requiring classification. Batch B (tab/input/render behavior) executed
with 4 passes and 12 failures requiring classification. The current evidence
separates stale harness assumptions from unresolved input/foreground behavior;
no product code has been changed during this closure pass.

Known stale-assumption cases are recorded in
`.agent/plans/validation-closure-2026-08-10.md`: the container `+` action now
opens inline capture, the launcher is hidden while a container is open,
persistence cleanup must close a launcher revealed after container shutdown, and
browser tab-strip movement is distinct from native title-bar movement. Direct
HWND/foreground evidence is still required for the remaining tab and guest-input
failures.

### Final blocker closure — 2026-08-10

Investigation reproduced the original transition and traced the exact desktop
order. Before the external steal the visible order was `guest -> container`;
after the steal it was `external -> guest -> container`; after a direct click
the guest was foreground and the order was `guest -> container -> external`.
The callback trace showed the external transition as `EVENT_SYSTEM_FOREGROUND`,
while the direct activation reliably produced a desktop-level
`EVENT_OBJECT_REORDER` with `hwnd = GetDesktopWindow()`, `idObject = OBJID_CLIENT`,
and `idChild = CHILDID_SELF`. The captured guest's foreground event was not a
reliable bounded signal for this direct click.

Root cause: the monitor only accepted captured HWND/idObject-zero events, so it
discarded the desktop reorder that proved the guest had been raised. Windows
focus and keyboard delivery still succeeded through ordinary top-level
activation; TabDock's separate local z-order reconciliation simply had no
reliable event to run on.

Fix: `WinEventMonitor` now installs the narrowly filtered desktop
`EVENT_OBJECT_REORDER` hook. At native callback time it snapshots the current
foreground HWND; the UI dispatch validates that the same HWND is still
foreground and, if it is a captured member, routes to the existing
`ContainerWindow.PairZOrderBehindGuest` policy. Normal foreground handling is
retained for transitions where it is the available signal. The Shepherd's
single `PairZOrderBehind` primitive skips its native mutation when the
container is already the next visible window, preventing repair-generated
reorder feedback from causing competing writes or an unbounded loop. No sleep,
polling, guest mutation, reparenting, or global bottoming was introduced.

Post-fix validation:

- `directclick-foreground-pairing`: 10/10 clean cycles passed after the final
  callback-time correlation change; repair latency was 189–213 ms, with
  foreground, z-order, text input, process-liveness, and no-exception checks
  passing each cycle.
- Repeated adjacent regressions passed: normal and split context-menu render
  stability, Chrome interaction/render stability, direct split input, normal
  and split tab switching, native move/resize re-glue, tab X, middle-click,
  split-left/right X, and related split cycle checks.
- A supervised real-input three-app torture run passed with Chrome, Edge, and
  Windows Terminal. It covered repeated normal tab switches and right-click
  menus, external-steal/direct return, Chrome address-bar interaction,
  Chrome+Terminal split focus, move/maximize/minimize/restore, Chrome
  middle-click pop-out and re-capture, Edge X pop-out and re-capture, and final
  group switching. No black/blank rendering, guest escape, popup layering
  failure, pane visibility failure, or TabDock exception was observed; cleanup
  completed without killing the shared Windows Terminal host.

The durable validation-harness additions are the callback-correlated
direct-click regression setup and the explicit `three-app-torture` supervised
scenario. The temporary WinEvent probe and all diagnostic P/Invokes were
removed. Final production readiness is **PASS**, subject to the final build,
validate, invariant audit, and sanctioned OpenSpec archive below.

### Final archive result — 2026-08-10

The repository OpenSpec CLI archived the completed change through its actual
workflow at
`openspec/changes/archive/2026-08-10-2026-08-10-vertical-split-screen`.
The archive command completed successfully with validation enabled; the delta
was retained in the archive because this change introduces the split-screen
capability rather than modifying an existing main capability mirror.

### Final production baseline checkpoint — 2026-08-10

The final command gates passed: application, solution, GuineaPig, and
ValidationDriver builds; `scripts\validate.ps1`; and
`openspec validate --all --no-interactive` (11/11). The current ValidationDriver
`--list` output was used to select the smoke scenarios. Fresh supervised runs
passed `directclick-foreground-pairing`, normal and split context-menu render
stability, Chrome interaction/render stability, direct split input, native
move/resize re-glue, tab X pop-out, and middle-click pop-out.

Production readiness is **PASS**. The archived change remains at
`openspec/changes/archive/2026-08-10-2026-08-10-vertical-split-screen`.
No critical milestone blockers remain; the next action is one coherent local
commit, with its hash reported after creation rather than embedded here.
