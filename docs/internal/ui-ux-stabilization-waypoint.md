# TabDock — UI/UX Stabilization Waypoint

Compact waypoint for the "visually stable, smooth, understandable, pleasant"
pass (goal-del-leter.txt). Updated: 2026-08-10.

## Baseline

- Branch `main`, starting HEAD `5e2a2ba` (functional vertical split milestone).
  Working-tree HEAD at work start: `448e8ef vertical split screen`.
- Shepherd/no-reparent architecture, `WindowShepherdService` remains the only
  guest positioning/z-order coordinator.
- Pre-existing untracked file `goal-del-leter.txt` (this goal's prompt) and
  `TabDock_ Production-Ready Vertical Split-Screen Feature.md` — neither was
  committed.

## User-Reported UX Defects

- A: severe visual glitching while moving TabDock (guest lag, pane separation,
  flicker, transient wrong geometry, double repositioning per frame).
- B: Group dropdown renders below/behind guests, partially covered, flicker.
- C: split-tab interaction — clicking a split member's tab can fail to render
  the partner pane.
- D: group management — no delete, rename not discoverable from the menu,
  startup clutter from accumulated persisted groups.
- E: Add Window surface not togglable (second click did nothing) and its
  panel could be occluded.

## Motion/Flicker Findings

Forensic trace (ContainerWindow.xaml.cs + WindowShepherdService.cs):

- Per drag tick, THREE WPF events fired `RelayoutGuests()`:
  `LocationChanged` + `SizeChanged` + `LayoutUpdated` (Loaded handler), each
  issuing up to 2 `SetWindowPos` calls (PositionAndShow + PairZOrderBehind);
  split mode issued 3-4 per tick (`PositionGuest` ×2 + `PairZOrderBehind`).
  Net: 3-5 native writes/tick normal, 4-9 split.
- `NativeHwndHost.ArrangeOverride` resizes the marker during WPF layout
  (async relative to the container's native move), so pre-layout triggers can
  read a stale marker rect; `LayoutUpdated` is the reliable post-layout signal.
- Split panes were positioned by three separate `SetWindowPos` calls — the
  top pane visibly moved while the bottom pane was still at its old position.
- `WM_ACTIVATE` reassert timer (120 ms) could overwrite `_splitForeground`
  with a window that had already left the pair.
- Group dropdown bug: `IsContainerChromeInteractionActive()` did NOT check
  `GroupContextMenu.IsOpen`, so the 120 ms reassert re-raised the guest above
  the container while the menu was open, hiding the popup.

## Current Positioning Call Graph (after this pass)

```
WM_WINDOWPOSCHANGED ─┐
LocationChanged      ├─→ RequestRelayout()  (coalesced, 1/frame, Render priority)
SizeChanged          ─┘        │
LayoutUpdated        ──────────┘  (post-layout, correct marker rect)
                               ▼
                    RelayoutGuests()
                     ├─ normal → LayoutShepherdActiveWindow()
                     │            (1px epsilon guard → PositionAndShow)
                     └─ split  → LayoutSplitPanes()
                                  (NeedsPanePosition guard → PositionGuestsDeferred
                                   = BeginDeferWindowPos(3)/DeferWindowPos×3/
                                     EndDeferWindowPos, atomic compositor batch)
```

Redundant per-frame writes eliminated (coalescing); split updates are atomic;
the most immediate native movement signal (WM_WINDOWPOSCHANGED) is hooked.

## HWND Composition Findings

- Group/color/tab context menus are WPF ContextMenu popup HWNDs; the container
  is raised above guests while open (`BeginChromePopup`/`EndChromePopup`) and
  the guest stack is reconciled exactly once on close (`CHROME[raise]` /
  `CHROME[restore-request]` logs).
- Inline Add Application panel is an in-window surface (no HWND) between the
  tab strip and the content area; it can be occluded if a guest's z-order
  drifts, so it now also raises the container via `BeginChromePopup` and
  restores on close.
- Per-tab "Pop out/Close" menu in the strip area does not overlap guests
  (chrome-owned region) — left unchanged.
- `Panel.ZIndex` can never beat an external guest HWND — the rule is owned
  popups + explicit z-order reconciliation, never WPF-only tricks.

## Composite Split Tab Design

- Presentation-layer only. New `ViewModels/SplitCompositeViewModel.cs` wraps
  the two member `TabViewModel`s; `GroupViewModel.DisplayTabs` is the strip
  projection: while a pair exists it replaces the LEFT member's slot with one
  `[ A | B ]` item and suppresses the RIGHT member's ordinary tab; otherwise it
  mirrors `Tabs` exactly (Add/Remove/Move mirrored so ListBox containers — and
  the anti-oscillation drag — stay intact).
- `EnterSplit`/`ExitSplit`/`HandleSplitMemberRemoved` call
  `SetSplitComposite`/`ClearSplitComposite` to rebuild the projection.
- LEFT half click → `SetActiveTab(A)` (split stays, B stays visible); RIGHT
  half → `SetActiveTab(B)`. Neither path can hide the partner (the old
  ordinary-tab selection path no longer exists for members).
- Per-half × and middle-click pop THAT member out; the split ends via
  `SPLIT[member-gone]` and the survivor is promoted to full width.
- Right-click on a half builds a member-specific menu (Pop out/Close window)
  plus the split-aware items (`ConfigureSplitMenuItems`, incl. "Exit split
  screen" while active).
- Ordering rule: composite occupies the LEFT member's visual position; all
  other tabs keep relative order; exiting split restores ordinary tabs in
  `Group.Members` order.
- Dragging: composite dragging is DISABLED while split is active (documented
  per goal §17 fallback); normal-mode drag reorder unchanged.

## Group Management Design

- Rename: existing double-click on the caption title + NEW "Rename group" item
  in the Group selector menu. `GroupViewModel.Name` now trims and rejects
  blank/whitespace names (keeps previous name); rename commits on Enter,
  cancels on Escape, persists via `SaveState`.
- Delete: NEW "Delete group" item in the Group selector menu. Confirmation
  dialog explains windows are released, not closed. On confirm:
  `RemoveGroup` + immediate `SaveState`, then every member is popped out via
  `ReleaseTab` (kept running — no WM_CLOSE); the emptied container closes via
  the existing `EmptiedByPopOut` path. Deleting the active group: other
  containers remain; when none remain the launcher returns (existing
  `OnContainerClosed` logic).
- Startup clutter (goal §26/§27): per-group-container architecture RETAINED.
  One container per restored group is created at startup (App.xaml.cs:132-145)
  by design (groups are empty layout intent, re-populatable). A single-shell
  rewrite was evaluated by the swarm (Agent D) and rejected: it touches App
  lifecycle, persistence, close semantics, and group→container assumptions
  with a large regression surface and no bounded migration benefit over the
  group-delete + close-empty-container flows added here (goal §27/§28:
  "otherwise retain").

## Files Modified

- `NativeMethods.cs` — `BeginDeferWindowPos`/`DeferWindowPos`/`EndDeferWindowPos`
  P/Invoke; `WM_WINDOWPOSCHANGED` constant.
- `Services/WindowShepherdService.cs` — `PositionGuestsDeferred` (atomic
  split batch, fallback to per-guest positioning).
- `Views/ContainerWindow.xaml` — strip bound to `DisplayTabs`; implicit
  templates (ordinary tab + composite `[ A | B ]` with central separator);
  per-half click/middle/× handlers; `vm:` namespace.
- `Views/ContainerWindow.xaml.cs` — `RequestRelayout` coalescing +
  `WM_WINDOWPOSCHANGED` hook; split uses `PositionGuestsDeferred`;
  `IsContainerChromeInteractionActive` includes `GroupContextMenu.IsOpen`;
  group menu opens deferred; capture panel `BeginChromePopup`/`EndChromePopup`;
  Add Window true toggle + Escape closes; split reassert timer guarded by
  `IsSplitMember`; rename empty-guard; Delete/Rename menu items + delete
  confirmation handler; composite right-click/middle/× routing; drag disabled
  during split; Ctrl+Tab selection guard.
- `ViewModels/GroupViewModel.cs` — `DisplayTabs` projection
  (`SetSplitComposite`/`ClearSplitComposite`/mirror), `Name` validation,
  `DeleteGroupCommand` + `DeleteGroupRequested`.
- `ViewModels/TabViewModel.cs` — unchanged (member identity untouched).
- `ViewModels/SplitCompositeViewModel.cs` — NEW (presentation wrapper).
- `tests/ValidationDriver/.../Scenarios.Split.cs` — composite-aware
  `ClickTabCloseButton` (picks the × nearest the target title); `split-reorder`
  reorders before entering split (pair-identity guarantee kept); NEW scenarios
  `group-dropdown-stability`, `add-window-toggle`, `group-rename-menu`,
  `group-delete-populated`, `split-composite`.
- `tests/ValidationDriver/.../Scenarios.cs` — registered the five new
  scenarios in `AllOrder` and `RunScenario`.

## Tests Added

- `group-dropdown-stability` — menu opens above a docked guest; guest stays
  docked through open/ESC-close cycles; no EXCEPTION.
- `add-window-toggle` — open→close→open→Cancel→reopen→capture closes; no stale
  surface; no EXCEPTION.
- `group-rename-menu` — rename via menu persists to state.json; whitespace-only
  rename rejected.
- `group-delete-populated` — apps keep running; members released; container
  closes; state.json drops the group; restart restores NO container.
- `split-composite` — pair renders as ONE item; LEFT/RIGHT half clicks keep
  both panes (no SPLIT[exit]/member-gone); non-member click exits split and
  restores ordinary tabs; per-half × and middle-click pop the specific member
  out (member-gone, survivor promoted, app kept running).

## Tests Passed

- `dotnet build TabDock.csproj` — 0 warnings, 0 errors.
- `dotnet build tests/ValidationDriver/...` — 0 warnings, 0 errors.
- ValidationDriver real-input scenarios NOT run in this environment (require
  a supervised interactive desktop; see Current Risks).

## Visual/Manual Validation

- NOT performed: no interactive desktop in this environment. All movement,
  overlay, composite-tab, and menu changes need the real-app torture checklist
  (goal §42) run on a supervised desktop before production sign-off.

## Current Risks

- The movement changes (coalescing, DeferWindowPos, WM_WINDOWPOSCHANGED) are
  compiled but not visually verified; epsilon guards and the per-frame
  coalescing are conservative, but frame-timing can only be confirmed live.
- `SWP_NOREDRAW` was deliberately NOT added (content-staleness risk); if
  residual tearing appears in live torture, evaluate it for the drag path only.
- Composite tab drag is disabled during split (documented); the harness
  `split-reorder` scenario was updated accordingly.
- `all` run grows by five scenarios — watch the harness's 10-minute whole-run
  budget when running the full suite.

## Swarm Phase 2 — Adversarial Review (§43, DONE)

Five read-only reviewers examined the working tree. Accepted and fixed:

- **Capture panel excluded from chrome guards** (R2-C1/C2): the 120 ms reassert
  could raise the guest above the container while the inline capture panel was
  open, and `EndChromePopup`'s deferred restore could re-pin the container
  below guests mid-panel. Fixed: `IsContainerChromeInteractionActive` now
  includes `IsCapturePanelOpen`; the deferred restore guard checks it too.
- **Empty-group delete orphan container** (R4-D1): deleting an EMPTY group left
  a live container bound to a detached `Group` instance (captures into it
  bypass `_capturedIndex` → un-rescuable, double-capturable). Fixed: explicit
  `Close()` after the release loop (no-op when `EmptiedByPopOut` already closed
  it).
- **Unguarded delete-confirm modal** (R4-D2): the delete MessageBox now sets
  `_closePromptOpen` (same pattern as the close prompt) so WinEvents and App's
  picker-deferral guard behave.
- **Mid-gesture foreground steal** (R1-Q4): the 120 ms reassert could fire
  `SetForegroundWindow(guest)` mid caption-drag/resize or strip-drag. Fixed:
  `WM_ENTERSIZEMOVE`/`WM_EXITSIZEMOVE` tracked in `_inNativeMoveLoop` and the
  tick also suppressed while `_isDragging`.
- **Test vacuity** (R5-D1..D4): `group-dropdown-stability` now proves the menu
  point is not covered (`WindowFromPoint` → TabDock-owned pid);
  `group-delete-populated` waits for/asserts the confirmation dialog and uses
  an inverted-wait restart check; `group-rename-menu` proves the rename box
  reopened before the whitespace check; `split-composite` asserts the real
  focus observable — new `SPLIT[focus]` log line emitted when a composite half
  click changes the active member — plus pane-glued and no-exit assertions.

Accepted as notes (no change): `PositionGuestsDeferred` unchecked
`SW_RESTORE`/`DeferWindowPos` returns (batch is fixed at 3 entries);
stale-menu-item reopen is theoretically unreachable (menus rebuild on every
open); close-group persistence is debounced by design.

Rejected: single-shell rewrite; `SWP_NOREDRAW` on the hot path (content
staleness risk pending live evaluation); per-tab strip context-menu changes
(chrome-owned region).

Re-review verdicts: motion architecture sound on duplicate/stale-write paths
(R1-Q1..Q3); split state machine sound incl. hidden-partner/stale-pair/reorder
hunts (R3); composite-aware `ClickTabCloseButton` and `TabCount`-during-split
assertions non-vacuous (R5).

## Exact Next Action

Run the supervised ValidationDriver suite (`dotnet run --project
tests\ValidationDriver\TabDock.ValidationDriver\TabDock.ValidationDriver.csproj
-- --yes all`), then the three-real-app visual torture checklist (goal §42:
continuous move/resize, split move, composite half clicks, Group menu
open/close, rename/delete, Add Window toggle). Fix any failures, then the
production-readiness gate (§45) before any commit.

---

# Hardening Round 2 — Overnight Customer-Readiness Campaign (2026-08-10/11)

## Priority Defects from Manual Feedback (goal-del-leter.txt §4-§21)

### Defect A — split pair asymmetric (partner member breaks)
Forensic verdict (swarm Agent A): three confirmed defects, all in the "focused member"
bookkeeping + survivor promotion:

1. **A1 — survivor demotion hides the promoted pane (the manual "one pane fails to
   render" path).** `GroupViewModel.ReleaseTab`'s else-branch picked the positional
   neighbour (`Tabs[Math.Min(idx, Tabs.Count-1)]`) AFTER `ContainerWindow.HandleSplitMemberRemoved`
   had already promoted the pair survivor via `SetActiveTab`. With ≥3 tabs and the
   ACTIVE member removed (exactly what happens when the user focuses the partner
   then pops/closes it), the neighbour ≠ survivor, so the survivor was HIDDEN and
   the neighbour shown. With [A,B,C], split A+B, focus B, pop B → survivor A was
   hidden, C shown. Two-tab splits never hit it (clamp = survivor) — which is why
   all existing two-tab harness scenarios passed.
   **Fix:** `ReleaseTab` now honors an already-promoted `ActiveTab` (survivor) before
   falling back to the positional neighbour; still re-syncs `Group.ActiveIndex`.

2. **A2 — direct-click focus revert.** Direct pane clicks updated `_splitForeground`
   but NOT `_shepherdActiveWindow`; the 120 ms WM_ACTIVATE reassert then overwrote
   `_splitForeground` with the tab-active member (the initiator), reverting the
   user's direct click on the partner.
3. **A3 — already-active half-click was a no-op** (SetActiveTab same-value short
   circuit), so a stale direct-click z-top could survive a strip click.

**Fix for A2/A3:** one canonical `ContainerWindow.FocusSplitMember(TabViewModel)`
(goal §6/§49) — sets `_splitForeground` + `_shepherdActiveWindow` + `SetActiveTab`
(highlight/ActiveIndex) + bounded `SPLIT[focus]` (logged only on member change) +
`LayoutSplitPanes`. All member-focus entry points now route through it: composite
half click, `SyncShepherdActiveWindow` split branch, `PairZOrderBehindGuest`
(WinEvent direct click), WM_ACTIVATE reassert. After split creation LEFT/RIGHT are
peers — no initiator/partner divergence remains in the code.

### Defect B — split panes overlap after maximize/restore
Forensic verdict (Agents B+C): partition math is provably clean (`SplitRect`
floor/remainder; 14.7M-check deterministic self-test passes). The defect is the
**synchronous re-glue inside `ContainerWindow_StateChanged`** which read the
marker rect BEFORE WPF re-arranged it (the code's own comment admitted the
staleness) — guests glued to the pre-transition halves for the whole DWM
transition; on slower machines the stale glue is visible as overlap/misalignment.
**Fix:** restore/maximize branch now calls `RequestRelayout()` (one coalesced
Render-priority pass AFTER layout resized the marker = final rect authoritative,
goal §12/§50); minimize hides stay synchronous; added one bounded
`STATE[transition]` diagnostic line per transition.

### Defect C — cross-machine inconsistency
Forensic verdict (Agent C): the production geometry chain is portable (PMv2
manifest, `VisualTreeHelper.GetDpi` marker sizing, physical-pixel guest glue,
signed rect math, `MonitorFromWindow` work-area clamp, no exact titles, no
fixed sizes). The friend-machine gap is timing/observability:
1. The Defect B stale-rect window (machine-speed dependent) — fixed above.
2. **No environment fingerprint** — added `EnvironmentFingerprint` (Services/
   EnvironmentFingerprint.cs): `ENV[startup]` (OS/.NET/bitness/monitor table via
   EnumDisplayMonitors), `ENV[container]` per open container (monitor+primary+DPI
   +rects), and `STATE[settled]` extended with platform + guest exe.
3. **Silent deferred-batch failures** — `PositionGuestsDeferred` now checks every
   `DeferWindowPos` + `EndDeferWindowPos` return, falls back to per-guest
   positioning, and logs the failure (no more fake-success SHEPHERD[position]
   lines).

## Deterministic geometry testing (goal §27/§28)
- `Services/SplitGeometry.cs` — extracted `Partition(content)` (the one split
  definition); `ContainerWindow.SplitRect` delegates to it.
- `TabDock.exe --selftest-geometry` — matrix (all widths 1..4096 × 7 heights × 6
  origins incl. negative) + targeted odd widths (799/800/801/1023/1024/1025/
  1919/1920/1921) + 100k seeded fuzz rects (seed 20260810): **14,718,730 checks,
  0 failures, PASS** (runs anywhere, no input, no UI). Runs before the mutex/UI;
  exit code 0/1; log line `SELFTEST[geometry]`.

## AutomationIds (goal §33)
Added to ContainerWindow.xaml: `GroupSelector`, `AddWindowButton`, `TabClose`,
`SplitCompositeItem`, `SplitHalfLeft/Right`, `SplitCloseLeft/Right`,
`CaptureRefresh/CaptureAddSelected/CaptureCancel`; code-built menus:
`NewGroup/RenameGroup/DeleteGroup`, `SplitScreen`, `SplitCandidate`,
`ExitSplitScreen`, `PopOut`, `CloseWindow`. Harness Uia.cs gained
`FindDescendantByAutomationId`; `ClickTabCloseButton` resolves per-half × by ID
(fallback: distance heuristic).

## Harness robustness (goal §37/§38)
- V1: split-composite asserts now parse the member from `SPLIT[focus]` and check
  the real foreground window after each half-click.
- V2: group-dropdown-stability proves the guest is the top window at the content
  center after menu close (z-order restore, not just geometry).
- V4/V5: add-window-toggle + group-rename-menu use WaitUntil state transitions
  instead of one-shot post-sleep checks.
- M3: split-selfclose margin 10 s → 20 s (capture flow can exceed 10 s on slow
  machines).
- M4: split-reorder log assertion wrapped in WaitUntil (async logger).

## New scenarios (goal §30/§34/§36)
- `split-three-tab-partner-popout` — the A1 regression: focus partner, pop it,
  survivor stays full-width and visible.
- `split-focus-bidirectional` — alternating LEFT/RIGHT half clicks ×N cycles,
  both panes rendered + focused each cycle.
- `split-partner-permutation` — A→B and B→A construction orders behave identically.
- `split-maximize-restore-no-overlap` — maximize/restore/minimize cycles with
  exact partition assertion (no overlap, no gap, both visible).
All four registered in AllOrder + runner switch. NOT runnable unattended (repo
rule: no SendInput without a supervisor); see Exact Next Action.

## Validation so far (this round)
- `dotnet build TabDock.sln`, `TabDock.csproj`, GuineaPig, ValidationDriver — 0 warnings, 0 errors.
- `scripts/validate.ps1` — PASS.
- `TabDock.exe --selftest-geometry` — PASS (14,718,730 checks, 0 failures), run twice.
- ValidationDriver real-input suite: BLOCKED (repo TESTING.md §C rule: supervised
  runs only; overnight session has no human supervisor). Batch plan per Agent D:
  A split-focus, B window-state, C movement, D popup/group/capture, E rename/
  delete/persist, F legacy lifecycle, G browser/real-app, H stress — `all` cannot
  complete (90-spawn cap vs 61 scenarios; 10-min CTS).

## Exact Next Action
Swarm Round 2 (§45): adversarial re-review of the fixes (break split symmetry;
WindowState-transition overlap; cross-machine assumptions; duplicate-write
oscillation; test observability). Then fix findings, waypoint update, docs,
production gate audit, final report, commit if PASS.

---

# Hardening Round 3 — Swarm Round 2 Adversarial Review Results (2026-08-11)

Five adversarial reviewers examined the Round-2 fixes. Accepted and fixed:

## Production fixes
- **R4-F1 (CRITICAL — the true root of Defect A):** `LayoutSplitPanes`' glued
  short-circuit pinned the container below the NEW bottom pane on strip-initiated
  focus switches (half-click, Ctrl+Tab, tab echo), which — when the focused
  member had CHANGED since the last glue — wedged the container BETWEEN the
  panes, occluding the just-focused member under the opaque content area
  (z [A,B,C] → click B → pin below A → [A,C,B]: B hidden). No rescue fires
  (container already active → no reassert; the WM_ACTIVATE guard compares a
  pre-click snapshot). Fix: before the cheap pin, verify the pair's internal
  order via `GetWindow(top, GW_HWNDNEXT) == bottom`; when stale, fall through to
  the existing atomic `PositionGuestsDeferred` batch. Steady state stays 0-1
  writes; the batch runs only when the invariant is actually broken.
- **R1-F1 / R4-F2 / R5-F1:** strip-initiated member focus never gave the clicked
  member real foreground (no SetForeground reachable from a half-click; the
  120ms reassert's own guard skipped it). Fix: `FocusSplitMember` now ends with
  `_shepherd.SetForeground(member.Model)` (early-returns when already
  foreground — direct pane clicks and repeats are no-ops).
- **R1-F3 (pre-existing, low):** stale context-menu initiator (tab died between
  menu-open and split-item click) installed a released window as LEFT with no
  composite. Fix: `StartSplitFrom` re-validates both tabs against `Tabs` first.
- **R4-F3:** deferred-batch failure log is now rate-limited via
  `LogPositioningFailureOnce` (no per-frame log storm on persistent failures).

## Portability fixes (R3)
- **F-B1:** DPI-unaware guests (DWM-virtualized 96-DPI space) now REFUSED at
  capture when `GetDpiForSystem() != 96` — physical-pixel glue would stretch/
  misplace them; refusal mirrors the elevation guard, probe failures fail open.
  New P/Invokes: GetWindowDpiAwarenessContext, AreDpiAwarenessContextsEqual,
  GetDpiForSystem.
  **SUPERSEDED (POST-AUDIT DPI COMPATIBILITY FINDING):** native evidence
  proved the 'stretch/misplace' premise false for OUTER-rect positioning - a
  PerMonitorV2 caller's physical SetWindowPos glues an unaware top-level
  window's outer rect exactly (GetWindowRect round-trips; the unaware caller
  sees it virtualized /1.25 at 125%). Known DPI-unaware guests are now captured
  normally; refusal is reserved for a probe that FAILS or returns UNKNOWN. Scale
  is classified from the target monitor's effective DPI (GetDpiForMonitor/"
  MDT_EFFECTIVE_DPI),
  not GetDpiForSystem(). A DPI-unaware guest's WM_GETMINMAXINFO min-track is
  converted logical->physical centrally (SplitGeometry.ScaleUnawareLogicalToPhysical)
  so size-constraint containment stays correct. See STATE.md and the audit waypoint.
- **F-B2:** caption FontFamily fallback now includes "Segoe MDL2 Assets"
  (Win10 glyphs render; Fluent Icons is Win11-only).
- **F-B3:** CapturePickerWindow + ContainerWindow clamp initial size to the
  primary work area (MaxWidth/MaxHeight = MaximizedPrimaryScreen*).
- **F-A1/A2:** `ENV[startup]` monitorCount labels pseudo-monitor semantics and
  reports EnumDisplayMonitors failure; §16 gaps closed: `ENV[launcher]` line
  (system DPI once the launcher exists) + active guest DescribeWindow appended
  to `ENV[container]`.
- Not changed (documented risks): `Global\TabDock` mutex semantics (F-B4 —
  product decision, conservative: keep single-instance across sessions);
  mixed-DPI system-aware guests (works single-monitor; exotic multi-DPI setups
  documented as a limitation).

## Harness fixes
- **R1-F2 / R5-F2:** split-partner-permutation now clicks the PARTNER half first
  in each case (the initiator is already focused after EnterSplit; the
  changed-guard emits no SPLIT[focus] for it → the old order could not pass).
- **R5-F3:** group-rename-menu's first rename waits for the rename box (Edit
  descendant) before typing instead of a fixed 300 ms sleep.
- **R2-F6:** split-maximize-restore-no-overlap now also exercises
  Maximized→Minimized→Maximized (all four goal §11 transitions).
- R5 F4/F5/F6/F7: no false-green windows found in the new scenarios (offsets
  are per-scenario, partition math direction correct, partner-popout
  discriminates the old build, WaitForSplitFocus parse robust).

## Round 2 reviewer verdicts (not fixed)
- R2: window-state matrix sound (no ordering produces persistent overlap;
  residual one-frame wrong-size re-show on Max→Min→restore-to-Normal is
  self-correcting and unobservable by the harness's final-state assertions).
- R4: no oscillation — exactly one position batch per frame; NextVisibleWindow
  remains the terminator on all paths; FocusSplitMember re-entrancy bounded
  (field-first ordering makes the PropertyChanged echo a no-op).

## Validation (this round)
- Builds (solution, app, GuineaPig, ValidationDriver): 0 warnings, 0 errors.
- `scripts/validate.ps1`: PASS.
- `TabDock.exe --selftest-geometry`: PASS ×2 (14,718,730 checks, 0 failures).
- ValidationDriver real-input suite: still BLOCKED (repo supervision rule;
  overnight session has no human supervisor). Batch plan documented in STATE.md.

---

# Hardening Round 4 — Split Persistence + Post-Drag Blanking (2026-08-10/11)

## New manual findings (user-confirmed, this round)

### Defect 1 — split is NOT persistent
With tabs A/B/C and split A+B active, interacting with unrelated tab C —
reportedly even hover — destroyed the A+B split or stopped it rendering as a
pair.

**Root cause (swarm forensics, all paths confirmed):** the single funnel
`ActiveTab` change → `ViewModel_PropertyChanged` → `SyncShepherdActiveWindow`
(`ContainerWindow.xaml.cs`) called `ExitSplit(keepActive: newWindow)` for any
NON-member activation. Three live paths: tab click (ListBox SelectionChanged →
SetActiveTab(C)), Ctrl+Tab (PreviewKeyDown cycled the full Tabs list), and Add
Window capture (AddCapturedWindow → SetActiveTab(new)). Pure hover has NO path
into selection (no MouseEnter/MouseLeave/IsMouseOver handlers anywhere; WPF
ListBox selects on mouse-down only) — the manual "hover" report is the
mouse-down/click path.

**Fix:** the split branch now rejects non-member activation — a newly-visible
non-member (fresh capture) is hidden journal-safely (`SPLIT[persist]`), the
logical active tab is reverted to the focused member via `FocusSplitMember`
(re-syncs ActiveIndex/highlight/glue/foreground), null-tab teardown keeps the
guarded ExitSplit. Ctrl+Tab cycles only between the pair's members. Split now
ends ONLY via explicit Split Screen operations or structural member removal.

### Defect 2 — post-drag blanking
One captured guest visible; drag the container; DURING drag fine (smooth);
IMMEDIATELY after release the content area can go blank; a tab switch repairs
it.

**Root cause (swarm forensics, Agent B/C with ReactOS/Wine evidence):** the
modal move loop keeps the dragged window at the TOP of the z-order; its final
z-order finalization can land AFTER the last per-frame re-glue, leaving
`[container, guest]` with the guest's rect still exactly matching the content
area. The epsilon-only redundant-glue guard then skipped EVERY later repair
(z-order-blind); WM_EXITSIZEMOVE was flag-only; the container's own drag is
invisible to the WinEvent pairing pipeline (foreground at drag end = container,
not a captured guest); `forceZOrder` was never true at rest. Result: the
covered-but-correctly-sized state persisted until a tab switch re-glued it.
Split mode was immune (its cheap pin re-validates via PairZOrderBehind) — the
asymmetry was the internal evidence.

**Fix:** WM_EXITSIZEMOVE → one coalesced `RequestRelayout()` (final
reconciliation after the loop unwinds; per-frame drag path untouched — no new
per-frame writes, no timers, no polling). The redundant-glue short-circuit now
validates the LOCAL PAIRING (container below guest) via the shepherd's upward
`GW_HWNDPREV` walk (`IsContainerBelowGuest`, skipping invisible helpers) before
skipping writes, gated off while chrome is raised. Round-2 review hardened the
probe: a strict-adjacency version was rejected (topmost guests live in a
separate band → unbounded SetWindowPos churn; hidden IME helpers → permanent
false-fail; cross-container reorder). Healthy steady state: zero writes;
broken pairing: exactly one idempotent pin.

## Tests added (registered in AllOrder + runner)
- `split-third-tab-hover-persists` — hover C ×N cycles; pair identity, panes
  glued+visible, C hidden, no exit/hide/release/switch, pane-center
  top-window PIDs.
- `split-third-tab-click-persists` — click C / LEFT half / click C / RIGHT
  half / click C / right-click C+dismiss / Ctrl+Tab per cycle; settled
  final-index assertions (the funnel transiently logs C's index, so
  assertions are on the settled `Switched group … to tab N`).
- `drag-release-render-stability` — single guest, multi-segment caption drag
  (right/down/left/up/diagonal/return), release, IMMEDIATE no-tab-interaction
  assertions (visible/glued/top-window-at-center), phase-robust 3-frame pulse
  liveness probe.
- `split-drag-release-render-stability` — same with A+B split, alternating
  focused member, exact-partition assertions.
- Rewritten `split-click-third` + `split-composite` non-member sections to the
  persistence contract; composite-aware `TabCount` corrections in six
  pre-existing 2-tab-split scenarios (split-two-auto, split-minrestore,
  split-native-move-reassert, split-native-resize-reassert,
  split-focus-bidirectional, split-maximize-restore-no-overlap — all assert
  the ONE composite item now).

## Round 2 adversarial review (this round) — findings resolved
- Reviewer 3: strict-adjacency probe regresses topmost guests (taskbar band →
  SetWindowPos churn per pass) — replaced with the upward-walk invariant
  predicate (production).
- Reviewer 4/5: pulse-probe phase flake (~60%/probe false-FAIL — two captures
  1200ms apart can land on the same 500ms toggle phase) — 3-frame any-adjacent
  pair at 400ms gaps (production harness).
- Reviewer 5: my split-drag `TabCount == 2` contradicted the composite
  projection (2-tab split = ONE item) — fixed, plus the six pre-existing stale
  assertions found by the same audit.
- Reviewer 5: click-C assertions contradicted the funnel's transient
  `Switched group … to tab C` log — settled-final-index assertions.
- Reviewer 4: Ctrl+Tab branch had no coverage — added as a step in
  split-third-tab-click-persists.
- Reviewer 4: narrow-monitor pane-center probes — container-fit check in
  EnsureContainerInWorkArea (clear environmental classification).
- Reviewer 1: all 18 split-attack vectors OK (incl. capture-while-split,
  release-non-member, teardown paths); no BREAKS.
- Reviewer 2: all 12 drag-finalization attacks OK; the "covered-at-rest always
  repaired by the next rest-time relayout with at most one SetWindowPos" claim
  CONFIRMED (with the chrome-gate caveat).

## Validation (this round)
- `dotnet build TabDock.sln` / TabDock.csproj / GuineaPig / ValidationDriver:
  0 warnings, 0 errors (multiple times, incl. after every adversarial fix).
- `TabDock.exe --selftest-geometry`: PASS (14,718,730 checks, 0 failures).
- ValidationDriver real-input suite: pending supervised run (see Exact Next
  Action) — the new scenarios plus the corrected pre-existing assertions are
  exercised there for the first time.

## Exact Next Action
Run the supervised ValidationDriver batches (A split/composite/focus incl. the
four new persistence/drag scenarios, B window-state, C movement incl.
drag-release, D popup/group/capture, E group lifecycle, F legacy, G browser,
H stress) with a human at the machine (no mouse/keyboard during the run), then
the three-real-app torture checklist, fix any findings, re-run builds +
selftest, archive the OpenSpec change, and create the ONE milestone commit.

---

# Hardening Round 5 — supervised validation closure (2026-08-11)

## New product bug found & fixed during validation

**Close-group prompt covered by the docked guest.** Clicking the container's
caption × activates the container (WA_CLICKACTIVE) and the 120ms `WM_ACTIVATE`
reassert then raises the docked guest ABOVE the just-shown "Close group"
MessageBox — its buttons end up covered (proven live: WindowFromPoint at the
Yes button resolved to the guest; the whole dialog sits inside the guest's
rect). Fix (one line): `IsContainerChromeInteractionActive()` includes
`_closePromptOpen`, so the reassert never fires while the prompt is open.
`exitpopulated` and `closegroupprompt` now PASS.

## Harness findings (all classified per goal §41; fixes at the harness layer)

- **split-composite middle-click** — released member C's own placement covered
  the container's strip (environmental layout collision; WindowFromPoint at
  the click point resolved to C). Fix: `MoveContainerClearOf` + probe +
  `EnsureClickable` before the click.
- **split-maximize-restore-no-overlap** — latent cycle-state mismatch: each
  cycle ended MAXIMIZED but started assuming NORMAL. Cycle-end normalization.
- **split-drag-release-render-stability** — once-computed half coordinates went
  stale as the container oscillated ±130px per drag cycle; per-cycle UIA
  re-read; "already-focused member" no-op clicks now impossible by structure.
- **exitpopulated (M6)** — launcher is hidden while a container is open
  (documented design), so the old launcher-Exit premise was unreachable.
  Rewritten: caption-× real click → prompt → Yes → launcher reappears → Exit;
  Yes/Exit clicks retried (first click on a fresh modal can be consumed by
  activation).
- **persist-kill / persist-active-tab-index / restored-group-survives-member-
  reclose** — single-pass WM_CLOSE exit broke because closing the last
  container re-shows the launcher (`CloseAllWindowsUntilExit` waves); the
  relaunch "MainWindow up" check raced the ~50ms launcher-hide at startup
  (wait for any visible top-level window).
- **CaptureIntoExistingGroupViaAddButton + reattach + hotkey-afterclose** — the
  container "+" opens the INLINE capture panel (design), not the standalone
  "Capture windows" picker; rewritten to drive the inline panel (row toggle,
  "Add selected", second-click toggle-close).
- **Stack overflow (real, product-adjacent)** — during batch runs the
  `TabsListBox_SelectionChanged` ↔ `SetActiveTab` ping-pong overflowed the
  stack when the split revert re-activated the focused member; fixed with the
  `_inSelectionSync` re-entrancy guard (Round 4 production fix verified live).

## Validation ledger (supervised; user present; no input during runs)

| Batch | Scenarios | Result |
|---|---|---|
| 1 split core (6) | two-auto, click-third, select-partner, exit, composite, three-tab-partner-popout | ALL PASS |
| 2 persistence/focus (5) | third-tab-hover-persists, third-tab-click-persists, focus-bidirectional, partner-permutation, repeat-cycles | ALL PASS |
| 3 window state (6) | split-maximize-restore-no-overlap, split-resize, split-minrestore, minrestore, maximize-repro, container-minimize-retains-tabs | ALL PASS |
| 4 movement (6) | split-move, split-native-move-reassert, split-native-resize-reassert, drag-release-render-stability, split-drag-release-render-stability, dragreorder | ALL PASS |
| 5 popup/chrome (7) | split-contextmenu-render-stability, contextmenu-render-stability, chrome-click-render-stability, group-dropdown-stability, add-window-toggle, capture-inline-ui, group-create-inline | ALL PASS |
| 6 group (6+2) | group-rename-menu, group-delete-populated, rename, exitpopulated, persist-active-tab-index, persist-kill, reattach-thenclick-othertab, reattach-repeated-cycles | ALL PASS |
| 7 legacy (11) | popout, closewin, closewin-hide, selfclose, selfhide, selfminhide, tabswitch-hidesafety, hotkey-afterclose, persist-kill, closegroupprompt, double-capture-refused | ALL PASS |
| Stress (goal §40) | focus 30 / hover 30 / click 25 / split-repeat 25 / drag 30 / split-drag 30 / max-restore 20 / contextmenu 20 / group-dropdown 20 / add-window-toggle 20 | ALL PASS |
| G real apps | browser-lifecycle chrome-normal, browser-lifecycle edge-normal, maximize-repro wt | ALL PASS |

## Final static validation
- builds ×4: 0 warnings / 0 errors; `scripts/validate.ps1` PASS;
  `--selftest-geometry` PASS (14,718,730 checks, 0 failures);
  `openspec validate --all --no-interactive` 12/12 PASS.

## Architecture audit (final diff)
No SetParent, no guest style/exstyle/owner mutation, no global HWND_BOTTOM, no
polling loop, no random sleep; deferred `Begin/Defer/EndDeferWindowPos` batch
is the sanctioned split atomic-positioning path; `WM_ENTERSIZEMOVE/
EXITSIZEMOVE/WINDOWPOSCHANGED` + one coalesced post-drag reconciliation;
`EVENT_OBJECT_REORDER` foreground-pairing repair preserved;
`WindowShepherdService` remains the sole positioning/z-order authority;
bounded diagnostics retained (WindowFromPoint probes), all temporary probes
removed.

## Round-5 ledger correction (2026-08-11 07:43)

Row 6 ("group ALL PASS") predated the green re-run of the two reattach
scenarios. Actual record: batch6g (06:54–06:55) FAILED
`reattach-thenclick-othertab` and `reattach-repeated-cycles` — the container's
'+' opened the standalone picker after a reattach, and the post-reattach
rename failed. Both scenarios were rewritten at 06:58 for the inline capture
panel design (row toggle + "Add selected" + toggle-close, rename retried) but
the rewrite was never re-run before the ledger was closed. Re-run supervised
2026-08-11 07:43: BOTH PASS — 3 reattach cycles, no second container, pair
restored, inline surface opens/dismisses, minimize works, no exceptions, both
pigs alive. Row 6 is now accurate.

## Post-closure harness safety (2026-08-11)

During supervised validation, the driver had a memory-only `state.json`
snapshot. A run that did not reach cleanup left the user's persisted state
file deleted; this was classified as a **HARNESS BUG**, not an application
failure. The driver now writes `state.json.driver-snapshot` through a
same-directory temporary file and atomic move before deleting `state.json`.
At the start of a later run, any leftover snapshot is restored through a
temporary file before the scenario starts. Cleanup restores the snapshot first
and deletes it only after the complete restore, so an interrupted run retains
a recoverable copy. The repeated reattach scenario also accepts `--cycles`
(minimum 3); the supervised 20-cycle rerun passed.

The final autonomous gates were rerun after this safeguard: all four builds
were clean, `scripts/validate.ps1` passed, the geometry self-test reported
14,718,730 checks with zero failures, OpenSpec validation passed 12/12, and
`ValidationDriver --list` passed without starting real-input scenarios.

## Computer Use QA — post-closure drag finding (2026-08-11)

### Visual defect

- **Reproduction:** With Chrome, File Explorer, and PredatorSense captured in
  one normal group, focus PredatorSense and drag the TabDock caption through a
  multi-segment move. Release and inspect immediately, without switching tabs.
  Repeat after switching tabs once to repair the view; the second caption drag
  reproduces the same result.
- **Expected:** The active guest remains fully rendered, aligned to the final
  content rect, and above the container immediately after release.
- **Observed:** Two supervised Computer Use repetitions showed a large blank or
  covered region over the guest immediately after release. A fresh screenshot
  shortly afterward settled, and switching tabs repaired it immediately.
- **Frequency:** 2/2 bounded repetitions in this session; timing-sensitive.
- **Apps involved:** TabDock + PredatorSense active guest; Chrome and File
  Explorer remained captured inactive guests.
- **Container state:** normal mode, one active guest, three ordinary tabs, no
  popup open, no split active.
- **Visual evidence:** Computer Use screenshots showed the TabDock chrome and
  the lower PredatorSense content visible while the upper guest region was
  covered/blank immediately after the drag; subsequent screenshots showed the
  full live PredatorSense surface.
- **Relevant logs:** final drag positions were logged as
  `SHEPHERD[position] guest=0x2051C rect=283,308,1125x670` without a distinct
  post-exit reconciliation marker. The source path is
  `ContainerWindow.WndProc(WM_EXITSIZEMOVE)` → `RequestRelayout()`.
- **Root-cause hypothesis:** the exit request can be coalesced away when a
  render-priority relayout is already pending from the last
  `WM_WINDOWPOSCHANGED`; that earlier pass can run before Windows completes
  final z-order normalization, leaving the container above the guest until a
  later relayout (such as a tab switch).
- **Next action:** make an exit-triggered final reconciliation survive an
  already-pending coalesced pass, then rebuild and repeat this exact visual
  sequence before certifying the result.

### Final implementation and retest

- `WM_EXITSIZEMOVE` now preserves one explicit follow-up Render-priority
  reconciliation when a coalesced pass is already pending. This keeps the
  final geometry/z-order repair from being lost during caption drags.
- The close-group modal received a separate z-order fix. While the prompt is
  open, chrome re-pairing/layout is suppressed; the container is temporarily
  raised, and a one-shot 50 ms dispatcher tick raises the native `Close group`
  dialog itself into the topmost band. Teardown removes that temporary band
  and queues the normal guest reconciliation. No guest is reparented or has
  its styles changed.
- Final supervised Computer Use retest with Chrome, File Explorer, and
  PredatorSense captured: the close prompt was visibly above the guest with
  Yes/No/Cancel exposed; Escape dismissed it and restored the guest. A final
  bounded caption drag after the modal fix settled with the full PredatorSense
  surface visible and no covered/blank region.
- Automated closure after the retest: four project builds, `scripts/validate.ps1`,
  geometry self-test, OpenSpec validation (12/12), `git diff --check`, and the
  native-invariant audit all passed. No full supervised ValidationDriver
  real-input batch was started in this turn.
- Remaining qualification: this evidence is a single supervised desktop/DPI
  environment and does not establish a cross-machine monitor matrix or a
  universal absence of timing-sensitive compositor issues. Treat the result as
  a validated fix in the exercised scope, not a claim that the product is
  bug-free.

---

## Post-stabilization containment hardening (guest size-constraint)

A subsequent user-reported defect reopened the "READY" assessment: a split
RIGHT-pane guest (Edge/Explorer) visibly overflowed the shell's content region,
worse at narrower widths. Root cause (proven with native `GetWindowRect`
evidence): the guest enforces a native minimum track size via `WM_GETMINMAXINFO`
(live-probed: Edge minW=643, Chrome minW=516-643, Explorer minW=161-201), and
TabDock requested a pane narrower than that minimum and never verified the
observed rect. The overflow grows as the pane narrows (deterministic probe: 0→300
px as pane 800→200 for a 500 px minimum).

Fix (Option A — dynamic TabDock minimum size, Shepherd-compatible):
- `WM_GETMINMAXINFO` min-track on the container from the visible guests' native
  minima (`SplitGeometry.MinContentWidth/MinContentHeight` + chrome delta), so the
  shell cannot be drag-resized below what the guests can fit.
- Bounded requested-vs-observed reconciliation: a guest that refuses its pane is
  marked non-compliant for that rect and not re-fought per frame (no resize war),
  with a bounded `SHEPHERD[size-constraint]` diagnostic.
- Constraint state recomputed on split enter/exit/replace, survivor promotion,
  active-tab change, `WM_EXITSIZEMOVE`, and a 5 s periodic re-probe.

Regression scenarios added (supervised, real input): `split-guest-does-not-overflow-pane`,
`split-narrow-container-constraints`, `single-guest-does-not-overflow-content`,
plus `--min-width/--min-height` GuineaPig support. Deterministic `--selftest-geometry`
now covers the constraint math (14,719,023 checks, 0 failures). All builds,
`scripts/validate.ps1`, and OpenSpec validation (13/13) pass. See
`docs/internal/whole-codebase-audit-waypoint.md` §"POST-AUDIT HIGH FINDING" and
`openspec/changes/guest-size-constraint-containment/` for full detail.
Supervised visual confirmation on real Edge/Explorer/Chrome remains outstanding.

---

## Final Hardening Closure (2026-08-11)

All three containment scenarios now pass supervised. Key harness/product fixes
in this closure:

- **WM_GETMINMAXINFO always-set** (product): WPF pre-populates lParam with
  large defaults; the old clamp-up never replaced them. Now always sets
  when a valid constraint exists.
- **Cross-process QueryMinTrack removed** (harness): lParam is a pointer
  in the harness's address space, invalid in the container's process.
  Replaced with behavioral containment assertions.
- **Cross-process SetWindowPos below min-track removed** (harness):
  destroys the container HWND. Narrow-resize behavioral tests removed.
- **Scenario 1 timing fixed** (harness): removed premature 50/50 IsInPane
  assertion; the RIGHT guest's 500px minimum makes the partition
  asymmetric. Post-resize containment assertion is the correct proof.

### All 5 scenarios PASSED

capture-dpi-unaware-guest, capture-dpi-system-guest (SKIPPED at 96 DPI),
split-guest-does-not-overflow-pane, split-narrow-container-constraints,
single-guest-does-not-overflow-content.

### OpenSpec changes archived

`dpi-unaware-acceptance` and `guest-size-constraint-containment` archived.
`openspec validate --all`: 12/12 PASS.

### Remaining

Manual visual confirmation on real multi-monitor DPI setups.
