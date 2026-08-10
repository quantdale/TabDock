# TabDock — Architecture Map

A compact system map for agents and developers working on TabDock: a C#/.NET 8 WPF utility
that merges independent top-level windows into a tabbed container under the **Shepherd**
model — guests are positioned, z-ordered, shown and hidden over the container's content
area, but *never reparented or restyled* (`Services/WindowShepherdService.cs:11-41`). No
third-party NuGet dependencies; all native interop goes through `NativeMethods.cs`. Every
claim below was verified against current source; citations use `path.cs:line`.

---

## 1. Startup sequence

`App` is the orchestrator (`App.xaml.cs:23`). Its constructor creates `LoggingService` and
attaches `AppDomain.UnhandledException` *before* `InitializeComponent()` so the earliest
failures are still logged (`App.xaml.cs:49-66`). `Application_Startup` (`App.xaml.cs:68`):

1. **Single-instance mutex** `Global\TabDock` — a second instance exits cleanly without
   touching shared state (`App.xaml.cs:80-85`, `AcquireSingleInstanceMutex` at `App.xaml.cs:615`).
2. **`CleanupStaleTempFiles`** — removes orphaned `state.json.tmp` / `hidden-windows.json.tmp`
   from a prior crashed atomic write (`App.xaml.cs:90`, `App.xaml.cs:642-674`).
3. **Service construction** (`App.xaml.cs:92-97`): `IconService`, `WindowShepherdService`,
   `PersistenceService`, `GroupManager(shepherd, persistence, log)`, `WinEventMonitor(...)`, `HotkeyService`.
4. **`GuestLifecycleService.Attach(_events)`** — all WinEvent policy behind one call (`App.xaml.cs:103-104`, `GuestLifecycleService.cs:51-60`).
5. **`MonitoringNeededChanged += SyncWinEventMonitor`** — the hooks gate (`App.xaml.cs:107`).
6. **`WindowShepherdService.RescueOrphanedWindows(_log)`** — journal replay before any groups
   open (`App.xaml.cs:108`, `WindowShepherdService.cs:626-673`).
7. **`_groups.RestoreState()`** — loads `state.json` (`App.xaml.cs:109`, `GroupManager.cs:82-88`).
8. **Main window + hotkey** — `MainViewModel` wiring, `MainWindow.Show()`, `_hotkey.Register()`
   (`MOD_CONTROL|MOD_ALT|MOD_NOREPEAT`, `HotkeyService.cs:59-61`); `HotkeyPressed` → picker (`App.xaml.cs:111-127`).
9. **Restored groups get containers** — each `OpenContainer(group)`; a failure is logged and
   skipped, never fatal (`App.xaml.cs:132-145`).
10. **`SyncWinEventMonitor()`** — no-op until the first capture; `TabDock startup complete.` (`App.xaml.cs:147-148`).

### Shutdown / emergency-release paths

`EmergencyReleaseAll` (`GroupManager.cs:395-419`) is the single release-all primitive;
`FlushJournalGuarded` (`App.xaml.cs:320-330`) forces the debounced journal clear via
`_shepherd.FlushJournal()` (`WindowShepherdService.cs:551-563`). Both run on every path:

| Path | What runs |
|------|-----------|
| `Application_Exit` (`App.xaml.cs:167`) | `EmergencyReleaseAll` → `SaveState` → `FlushJournalGuarded` → dispose events/hotkey/mutex/logger (`App.xaml.cs:170-189`) |
| `Application_DispatcherUnhandledException` (`App.xaml.cs:192`) | `SaveStateGuarded` → `FlushJournalGuarded` → `EmergencyReleaseAll` → `Shutdown(1)` (`App.xaml.cs:194-207`) |
| `CurrentDomain_UnhandledException` (`App.xaml.cs:210`) | Terminating: log-only (any thread; runtime is tearing down, `App.xaml.cs:215-221`). Non-terminating: marshals `SaveStateGuarded` + `FlushJournalGuarded` + `EmergencyReleaseAll` to the UI dispatcher with a 1 s deadline (`App.xaml.cs:226-252`) |
| `Application_SessionEnding` (`App.xaml.cs:254`) | `SaveStateGuarded` + `FlushJournalGuarded` + `EmergencyReleaseAll`, then clears `Members` and stops hooks so a cancelled logoff leaves a coherent state (`App.xaml.cs:257-295`) |
| `Application_Startup` catch (`App.xaml.cs:150`) | Same trio + `Shutdown(1)` (`App.xaml.cs:150-164`) |

`FlushJournalGuarded` call sites: `App.xaml.cs:154`, `:182`, `:197`, `:237`, `:260`.
`ContainerWindow.IsAppShuttingDown` is set before every shutdown so `Closing` skips its
Yes/No prompt (`App.xaml.cs:152/169/194/212/257/544`, `ContainerWindow.xaml.cs:97,314-317`).

---

## 2. Capture lifecycle

### Capture

Trigger: the `Ctrl+Alt+G` hotkey (`HotkeyService.cs:97-106`) or a container's "+" button →
`ShowCapturePicker` (`App.xaml.cs:396-431`; re-entrancy-guarded via `_pickerOpen` and the
close-prompt check, `App.xaml.cs:401-431`). The picker enumerates top-level windows with
cheapest-first filters: visible → not tool-window → titled → not own window
(`GroupManager.IsOwnWindow`, `GroupManager.cs:129-149`) → not already captured
(`GroupManager.cs:156-159`) → not DWM-cloaked (`CapturePickerViewModel.cs:79-135`).

On OK, `App` resolves/creates the target group + container, then calls
`container.CaptureWindow(hwnd)` (`ContainerWindow.xaml.cs:697-717`), which re-checks the
no-nesting and already-captured rules, then `_shepherd.Capture(hwnd, out error)`
(`WindowShepherdService.cs:89-163`):

- Refuses dead HWNDs and own-process windows (`WindowShepherdService.cs:92-103`).
- **Elevation check fails closed** — indeterminate target elevation + non-elevated TabDock
  refuses the capture (`WindowShepherdService.cs:105-131`).
- Snapshots `WINDOWPLACEMENT` (`HasValidPlacement`, `WindowShepherdService.cs:133-139`), bounds,
  title, exe path; disables DWM transitions for the captured lifetime (`WindowShepherdService.cs:159,165-169`);
  logs `Shepherd-captured`.

`GroupViewModel.AddCapturedWindow` (`GroupViewModel.cs:168-177`) adds to `Group.Members` and
`Tabs` and activates the new tab; `GroupManager`'s `CollectionChanged` hook maintains the O(1)
HWND→member index (`GroupManager.cs:184-292`) and raises `MonitoringNeededChanged`, which
installs the hooks immediately (`App.xaml.cs:348-361`).

### Tab switch

`GroupViewModel.SetActiveTab` (`GroupViewModel.cs:137-144`) → `GroupManager.SwitchActiveTab`
(`GroupManager.cs:304-311`, logs `Switched group ...`) → `ActiveTab` change →
`ContainerWindow.SyncShepherdActiveWindow` (`ContainerWindow.xaml.cs:750-768`): **position/show
the new guest first** (it covers the outgoing one), then `_shepherd.Hide(old)` if still a
member (`WindowShepherdService.cs:247-260` — journals *before* hiding). Positioning runs
`LayoutShepherdActiveWindow` (`ContainerWindow.xaml.cs:775-818`): content rect off the
`NativeHwndHost` marker (`GetContentAreaScreenRect`, `ContainerWindow.xaml.cs:827-842`),
skip redundant re-glues when the guest already covers it within 1 px, then `PositionAndShow`
(`WindowShepherdService.cs:179-215`): `SW_RESTORE` if iconic/zoomed, `SetWindowPos`
`HWND_TOP` + `SWP_SHOWWINDOW`, `PairZOrderBehind(container, guest)`
(`WindowShepherdService.cs:225-235`), `JournalClear`, `SHEPHERD[position]`.

### Movement synchronization

Container move/resize re-glues the guest(s) to the content marker. Every
trigger — the native `WM_WINDOWPOSCHANGED` (hooked in `WndProc` so the guest is
re-glued in the same native message flow as the container's own movement),
`LocationChanged`, `SizeChanged`, and the post-layout `LayoutUpdated` — funnels
into one coalesced `RequestRelayout()` (a pending flag + a single
Render-priority dispatcher callback), so at most one batch of native
reposition calls is issued per WPF frame instead of 3-5, and the batch always
runs after WPF has arranged the content marker to its final rect. This removed
the per-frame double-move/backward-jump that made dragging look glitchy.

### Vertical split screen (two guests)

From a captured tab's context menu, TabDock can display exactly two guests
simultaneously in a LEFT/RIGHT vertical split (the Shepherd model is unchanged —
both stay independent top-level HWNDs, never reparented/restyled). The split is
owned by `ContainerWindow`:

The container caption's Group menu switches to an already-open group or creates
one in the existing shell. The launcher is hidden while a container is open and
remains only as the no-group/global-hotkey fallback. Routine Add App capture is
an in-window panel; the standalone picker remains for the fallback path.

- **State** — `_splitLeft`/`_splitRight` hold `CapturedWindow` references
  (identity, not index, so the pair survives tab reordering); `_splitForeground`
  tracks which member is z-order-top. Split is runtime-only (not persisted).
- **Composite tab (presentation only)** — the strip renders the pair as ONE
  visual item `[ A | B ]` (a subtle central separator) instead of two unrelated
  tabs: `GroupViewModel.DisplayTabs` is the strip projection
  (`SplitCompositeViewModel` wraps the two member `TabViewModel`s; the RIGHT
  member's ordinary tab is suppressed while the pair exists; the composite
  occupies the LEFT member's visual position; exit restores ordinary tabs in
  `Group.Members` order). Domain identity is never merged — the two
  `CapturedWindow`s remain separate members. The projection mirrors `Tabs`
  exactly when no split is active (Add/Remove/Move mirrored so ListBox
  containers survive reorders).
- **Enter/exit** — `EnterSplit(left, right)` / `ExitSplit(keepActive)` route
  through the context menu (`ConfigureSplitMenuItems`: disabled below two tabs,
  direct action at exactly two, submenu at three+, `Exit split screen` when
  active). Clicking a composite HALF keeps the split and focuses that member
  without hiding the partner (no ordinary tab-selection path can misinterpret a
  member as needing to hide the other). **The pair is the persistent selected
  tab-strip unit**: clicking or hovering a NON-paired tab leaves the pair
  untouched — `SyncShepherdActiveWindow` rejects the non-member activation,
  hides it only when it was newly visible (a fresh capture — journal-safe, one
  bounded `SPLIT[persist]` line), and reverts the logical active tab to the
  focused member via `FocusSplitMember`. Split ends ONLY from an explicit
  `Exit split screen` / Split Screen replacement, or a structural member
  removal (pop-out, ×, middle-click, self-close/hide, group deletion). Ctrl+Tab
  while split cycles between the two members only.
  Per-half × / middle-click pop THAT member out; right-click builds a
  member-specific menu (Pop out / Close window plus the split-aware items).
  Composite dragging is disabled while split is active (the composite is not a
  drag unit; normal-mode drag reorder is unchanged). Departing members are
  hidden via `_shepherd.Hide` (journal-before-hide preserved); neither member
  is ever released by a split transition.
- **Layout** — `LayoutSplitPanes` derives LEFT/RIGHT halves from the content
  rect via `SplitGeometry.Partition` (`leftW = Width/2`, right gets the
  remainder; no DPI conversion; the single deterministic definition, also
  exercised by `--selftest-geometry`) and establishes the local order
  `foreground guest → partner guest → container`. The 1px redundant-glue guard
  is per-pane; when a re-glue is needed both panes plus the container pin are
  written in ONE compositor transaction (`PositionGuestsDeferred`:
  `BeginDeferWindowPos`/`DeferWindowPos`/`EndDeferWindowPos`, with per-entry
  return checks and a per-guest fallback on failure) so the panes never
  visibly separate mid-write. The container is paired below the partner
  without being pushed behind unrelated desktop windows. When both panes are
  already glued, the cheap pin runs ONLY after verifying the pair's actual
  order (`GetWindow(top, GW_HWNDNEXT) == bottom`) — a strip-initiated focus
  switch (which raises no guest natively) would otherwise wedge the container
  between the panes and occlude the just-focused member.
- **Focused member (one canonical operation)** — `FocusSplitMember(member)`
  is the ONLY path that focuses a member of the active pair: it updates
  `_splitForeground` (z-top) and `_shepherdActiveWindow` (logical active +
  tab highlight + `Group.ActiveIndex`), emits the bounded `SPLIT[focus]`
  diagnostic (only when the focused member changes), re-glues both panes with
  the member on top, and grants the member real foreground. Every entry point
  routes through it: composite half click, active-tab sync, direct guest click
  (WinEvent), and the WM_ACTIVATE reassert — so LEFT and RIGHT are peers after
  split creation and no initiator/partner asymmetry can arise. It never
  changes split membership or hides the partner.
- **Survivor promotion** — when a member leaves the group the split ends and
  the survivor is promoted to the single visible guest; `ReleaseTab` honors
  that promotion (no positional neighbour pick may hide or displace the
  survivor afterwards).
- **Window-state reconciliation** — minimize hides the visible guest(s)
  synchronously; restore/maximize re-glues through the coalesced post-layout
  pass (`RequestRelayout`), never synchronously against the pre-transition
  marker rect, so the FINAL content rect is authoritative for every
  `Normal↔Maximized↔Minimized` combination. One bounded `STATE[transition]`
  line records the pre-layout rect for diagnosability.
- **Drag-end reconciliation** — `WM_EXITSIZEMOVE` (the container's own native
  move/resize loop end) now schedules ONE coalesced `RequestRelayout()` instead
  of only clearing `_inNativeMoveLoop`. The modal move loop keeps the dragged
  window at the top of the z-order, and its final z-order finalization can land
  AFTER the last per-frame re-glue — leaving the container above its guest with
  the guest's rect still exactly matching the content area (the redundant-glue
  guard would otherwise skip every later repair, blanking the content area
  until a tab switch re-glued it). The post-loop pass re-validates both
  geometry and the local pairing; the per-frame drag path itself is untouched
  (still one coalesced Render-priority pass, no new per-frame writes).
- **Lifecycle** — split-aware `SyncShepherdActiveWindow`, `StateChanged`,
  `NoteGuestMoveSize` (drag-out measured against the member's own pane),
  `RestoreMinimizedWindow`, `PairZOrderBehindGuest`, and the WM_ACTIVATE
  reassert. `GuestLifecycleService.OnWindowHidden` treats a hide of either split
  member as guest-initiated teardown (both are visible in split). A split member
  leaving the group (pop-out, drag-out, self-close, self-hide) ends the split and
  promotes the survivor to the single visible guest.
- **Primitives** — `WindowShepherdService.PositionGuest` (position one guest at a
  z-order slot), `PositionGuestsDeferred` (atomic two-guest + container batch),
  `SetForeground` (foreground without repositioning),
  `RaiseContainerForChrome` (temporary TabDock UI), and `PairZOrderBehind` (the
  single local container/guest ordering primitive).

### TabDock z-order policy

TabDock-owned temporary UI has an explicit lifecycle. On opening a context menu,
color menu, or owned capture surface, the container is raised without hiding or
removing any guest. When that surface closes, the container reconciles the guest
stack once: normal mode restores `guest → container`; split mode restores
`foreground guest → partner guest → container`. Logical visibility does not
change during popup interaction. Guests remain independent top-level windows and
ordinary unrelated desktop windows are not displaced by a global `HWND_BOTTOM`
operation.

**Local pairing invariant.** The single-guest redundant-glue guard
(`LayoutShepherdActiveWindow`) validates geometry AND the local pairing before
skipping its native writes: the container must sit BELOW the guest. The check is
the shepherd's upward `GW_HWNDPREV` walk
(`WindowShepherdService.IsContainerBelowGuest`, skipping invisible helper
windows) — NOT a strict-adjacency probe, which would fail forever (and churn a
`SetWindowPos` per relayout pass) for a `WS_EX_TOPMOST` guest living in a
separate z-order band, would false-trigger on hidden IME helpers, and would
reorder unrelated TabDock containers. The same invariant guards
`PairZOrderBehind`'s no-op path, so a healthy steady state issues ZERO native
writes and a broken pairing heals with exactly one `SetWindowPos` (idempotent
afterwards). The repair is skipped while chrome is intentionally raised above
the guests (context menu / color menu / group menu / capture panel / rename
box / close-group confirm dialog); the popup-close path reconciles the stack
with `forceZOrder`. The close-group/delete-group confirm dialog is included in
that guard (`_closePromptOpen` in
`ContainerWindow.IsContainerChromeInteractionActive`): without it the 120ms
`WM_ACTIVATE` reassert that follows clicking the container's × raises the
docked guest above the just-shown MessageBox and covers its buttons (found
live during supervised validation; `exitpopulated`/`closegroupprompt` cover
it).

Log vocabulary: `SPLIT[enter]`, `SPLIT[exit]`, `SPLIT[replace]`,
`SPLIT[member-gone]`, `SPLIT[persist]` (a newly-visible non-member hidden to
preserve the pair's visible set).

### Release

`GroupManager.ReleaseTab` (`GroupManager.cs:330-353`) removes the member from `Group.Members`
first (the index drops it via `CollectionChanged`), then `_shepherd.Release(cw, show)`
(`WindowShepherdService.cs:328-419`):

- Window already gone → `JournalClear` + log (`WindowShepherdService.cs:330-335`).
- `show:false` (guest-initiated hide): `JournalClear(immediate: true)` **before** `SW_HIDE`,
  transitions re-enabled (`WindowShepherdService.cs:337-355`).
- `!HasValidPlacement`: bounds fallback — `SetWindowPos(OriginalBounds)` + `SW_SHOW` +
  `SetForegroundWindow` (`WindowShepherdService.cs:357-386`).
- Normal: `SetWindowPlacement(OriginalPlacement)` (falls back to bounds `SetWindowPos` on
  failure), `ShowWindow(showCmd)`, `SetForegroundWindow`, `JournalClear`
  (`WindowShepherdService.cs:388-418`).

Release-by-reference for WinEvent teardown: `GroupManager.ReleaseMember` (`GroupManager.cs:362-367`)
via `RemoveDeadMember` (`GuestLifecycleService.cs:226-258`), which prefers
`container.ReleaseCapturedWindow` (`ContainerWindow.xaml.cs:732-737` → `GroupViewModel.ReleaseTab`,
which keeps the active tab active, `GroupViewModel.cs:187-225`).

### Empty-group container close

`RemoveDeadMember` closes the container when `Members.Count == 0` and removes the group only
if `PersistedTabs.Count == 0` (a restored group carries saved layout intent)
(`GuestLifecycleService.cs:237-257`). Popping out the last tab triggers `EmptiedByPopOut` →
container `Close` (`ContainerWindow.xaml.cs:136-146`, `GroupViewModel.cs:204-208`);
`App.OnContainerClosed` removes only empty, never-repopulated groups (`App.xaml.cs:579-608`).

### Pop-out paths

- Tab-strip drag leaving the container bounds (`ContainerWindow.xaml.cs:1014-1026`).
- Dragging or resizing the guest by its own real title bar/edge is always
  re-glued to its assigned pane (`NoteGuestMoveSize`; events from
  `OnGuestMoveSize`). Native movement never releases a tab.
- Tab context-menu **Pop out** (`GroupViewModel.cs:243`).

---

### Cross-machine hardening

- **DPI-aware guests only at non-100% scale.** Capture refuses a DPI-unaware
  guest when `GetDpiForSystem() != 96` (its DWM-virtualized 96-DPI coordinate
  space cannot be glued with physical-pixel rects); the awareness probe is
  fail-open. System-aware guests work on single-DPI systems (mixed-DPI
  multi-monitor is a documented limitation). The Shepherd physical-pixel
  convention itself is unchanged.
- **Environment fingerprint** (`Services/EnvironmentFingerprint.cs`): one
  `ENV[startup]` line (OS version/build, .NET runtime, bitness, full monitor
  table with bounds/work/primary via `EnumDisplayMonitors`), one
  `ENV[launcher]` line (system DPI once the launcher exists), one
  `ENV[container]` line per open container (rects, window state, active
  monitor, DPI, guest), and the `STATE[settled]` snapshot carries the platform
  and guest executable. Bounded — never per-frame — so customer logs are
  self-describing.
- **Deterministic partition self-test**: `TabDock.exe --selftest-geometry`
  runs `SplitGeometry.RunSelfTest` (matrix widths 1..4096 × heights × origins
  incl. negative, odd widths, 100 k seeded fuzz rects, seed 20260810) with no
  UI/input, logs `SELFTEST[geometry]`, exits 0/1.

## 3. WinEvent pipeline: event → handler → effect

Hooks installed in `WinEventMonitor.Start` (`WinEventMonitor.cs:88-96`): `EVENT_OBJECT_DESTROY`,
`EVENT_SYSTEM_FOREGROUND`, `EVENT_OBJECT_REORDER`, `EVENT_OBJECT_NAMECHANGE`, `EVENT_SYSTEM_MINIMIZESTART`,
`EVENT_OBJECT_HIDE`, and one ranged `EVENT_SYSTEM_MOVESIZESTART..END` hook. A partial install
unwinds and reports failure (`WinEventMonitor.cs:98-107`).

The native callback filters `idObject/idChild != 0` and zero HWNDs, then the **direct-HWND-match**
`IsCapturedWindow` filter — never `GetAncestor`, useless under Shepherd (guests are their own
root) and invalid for already-destroyed windows (`WinEventMonitor.cs:152-167`). Survivors are
dispatched via **`SynchronizationContext.Post` — never `Send`** (handlers must observe the UI
state *after* the causing operation; `WinEventMonitor.cs:170-177`). `Raise` re-verifies
`_running && IsCapturedWindow` against HWND recycling, then switches on event type
(`WinEventMonitor.cs:185-218`).
The desktop `EVENT_OBJECT_REORDER` path is the one deliberate exception to
direct guest-HWND filtering: Windows reports the desktop client object
(`GetDesktopWindow`, `OBJID_CLIENT`, `CHILDID_SELF`) for top-level z-order
changes, so the callback snapshots `GetForegroundWindow()` and the UI handler
revalidates that snapshot before pairing a captured guest.

| WinEvent | Handler (`GuestLifecycleService.Attach`, `GuestLifecycleService.cs:51-60`) | Effect |
|---|---|---|
| `EVENT_OBJECT_DESTROY` | `OnWindowDestroyed` (`GuestLifecycleService.cs:62-69`) | Log `destroyed; removing its tab` → `RemoveDeadMember(show: true)` |
| `EVENT_OBJECT_HIDE` | `OnWindowHidden` (`GuestLifecycleService.cs:71-108`) | Guest-initiated-hide classification: rejected unless the hider is the **active** tab (tab-switch hides are excluded because the active tab already moved), HWND still alive, not visible again, and container not minimized (minimize-hide guard). Passes → log `hid itself` → `RemoveDeadMember(show: false)` |
| `EVENT_SYSTEM_MINIMIZESTART` | `OnWindowMinimized` (`GuestLifecycleService.cs:110-120`) | Log → `container.RestoreMinimizedWindow` — 200 ms deferred, re-checks iconic + visible + still active before `SW_RESTORE` (`ContainerWindow.xaml.cs:875-914`) |
| `EVENT_SYSTEM_MOVESIZESTART/END` | `OnGuestMoveSize` (`GuestLifecycleService.cs:141-158`) | `container.NoteGuestMoveSize` — end only; native movement/resize is re-glued to the assigned pane and never releases a tab (`ContainerWindow.xaml.cs`) |
| `EVENT_SYSTEM_FOREGROUND` | `OnForegroundChanged` (`GuestLifecycleService.cs:129-135`) | `container.PairZOrderBehindGuest` → `shepherd.PairZOrderBehind` re-pins the container behind the guest (`ContainerWindow.xaml.cs:854-864`, `WindowShepherdService.cs:225-235`) |
| `EVENT_OBJECT_REORDER` (desktop client) | `OnZOrderChanged` (`GuestLifecycleService.cs`) | Callback-time foreground HWND is revalidated on the UI thread; if it is a captured guest, routes through the same `PairZOrderBehindGuest` policy to repair direct-click adjacency |
| `EVENT_OBJECT_NAMECHANGE` | `DebounceNameChanged` (`GuestLifecycleService.cs:166-184`) | Per-HWND 250 ms coalescing timer → `HandleNameChanged` (`GuestLifecycleService.cs:186-215`): custom label wins, empty titles ignored, unchanged titles skipped, else update `OriginalTitle` + `RefreshTabTitle` |

**Invariants** (see `docs/internal/perf-2026-07-25.md`):
- **Post, never Send** — `WinEventMonitor.cs:170-177`.
- **O(1) resolution** — handlers resolve via `GroupManager.TryGetCapturedMember` (`GroupManager.cs:168-180`),
  one probe; never scan `Groups`.
- **Hooks gated on `IsMonitoringNeeded`** (`GroupManager.cs:70`) — `App.SyncWinEventMonitor`
  (`App.xaml.cs:348-376`): install immediately on first capture, removal deferred one dispatcher turn.

---

## 4. Persistence & journal

Two distinct files in `%APPDATA%\TabDock\`:

### `state.json` — layout intent (metadata only)

`PersistenceService` (`PersistenceService.cs:14-250`): group id/name/accent/active index plus
per-tab exe path, title, custom label, bounds, `WasMaximized` (`PersistenceService.cs:36-66`).
**No HWNDs, no app content** — restored groups start empty as layout intent
(`PersistenceService.cs:164-186`). Saves are debounced ~1 s (`GroupManager.cs:104-117`) and
**skip the write when the JSON is unchanged**, but only if the file still exists (`_lastSavedJson`,
`PersistenceService.cs:19-21,99-100`). Writes are atomic: `.bak` copy, `.tmp` + `File.Move`
(`PersistenceService.cs:104-113`); a corrupt file is quarantined on load (`PersistenceService.cs:218-233`).

### `hidden-windows.json` — crash-recovery journal

`WindowShepherdService` (`WindowShepherdService.cs:64-65,421-675`). Entries:
HWND + PID + exe path (`WindowShepherdService.cs:440-445`). Ordering rules:

- **Hide journals before hiding** — `Hide` calls `JournalHide` then `ShowWindow(SW_HIDE)`
  (`WindowShepherdService.cs:247-252`); a force-kill in between never orphans an unjournaled
  guest. `JournalHide` writes **synchronously** — TerminateProcess bypasses every exit handler
  (`WindowShepherdService.cs:423-453`).
- **Show/release clears** — `JournalClear` after any path that leaves the guest genuinely
  visible; debounced 300 ms (`RequestJournalSave`, `WindowShepherdService.cs:455-541`).
- **`immediate: true` where the guest ends intentionally hidden** (release `show:false`) — a
  stale entry must never make rescue un-hide a deliberately hidden window (`WindowShepherdService.cs:337-348,455-473`).
- **`FlushJournal`** forces a pending debounced clear; called from every App exit/crash path
  (`App.xaml.cs:154,182,197,237,260`, `WindowShepherdService.cs:551-563`).
- **`RescueOrphanedWindows`** (startup, `App.xaml.cs:108`) re-shows entries whose PID + exe
  still match, then deletes the journal unconditionally (`WindowShepherdService.cs:626-673`);
  empty/corrupt journals are removed, never re-read (`WindowShepherdService.cs:565-585,630-639`).

---

## 5. Log-line index

`%APPDATA%\TabDock\logs\TabDock.log`, 1 MB rotation (`LoggingService.cs:10,31`). Lines that
tests and agents may rely on:

| Log line (substring) | Emitter | Meaning |
|---|---|---|
| `SHEPHERD[position] guest=0x… rect=…` | `WindowShepherdService.cs:214` | Guest (re)positioned/shown; per mouse tick during container drag |
| `Shepherd-captured 0x…` | `WindowShepherdService.cs:161` | Capture succeeded (new member) |
| `Shepherd-released 0x…` | `WindowShepherdService.cs:353` / `:384` / `:418` | Release: guest-initiated-hidden / bounds-fallback / normal |
| `SHEPHERD[hide] guest=0x…` | `WindowShepherdService.cs:259` | Inactive tab hidden |
| `SHEPHERD[bring-to-front]` | `WindowShepherdService.cs:299` | Foreground re-assert after container activation |
| `SHEPHERD[re-glue]` | `ContainerWindow.xaml.cs` | Native move/size ended outside the assigned pane |
| `SHEPHERD[rescue]` | `WindowShepherdService.cs:661` / `:667` | Journal replay at startup |
| `SHEPHERD[position-fail]` | `WindowShepherdService.cs:61` | First positioning failure per HWND (UIPI/dead HWND) |
| `Switched group {id} to tab {i}` | `GroupManager.cs:309` | Active-tab change |
| `Reordered tab {old}->{new} in group {id}` | `GroupManager.cs:326` | Drag reorder committed |
| `Released tab {i} from group {id}` | `GroupManager.cs:351` | Tab released |
| `… destroyed; removing its tab.` | `GuestLifecycleService.cs:67` | Destroy teardown |
| `… hid itself (tray-style close); releasing its tab hidden.` | `GuestLifecycleService.cs:106` | Guest-initiated hide teardown |
| `… minimized; restoring it inside its tab.` | `GuestLifecycleService.cs:115` | Minimize restore |
| `WinEvent: title changed for 0x…` | `GuestLifecycleService.cs:209` | Debounced title refresh |
| `WinEventMonitor started (hooks: …)` / `WinEventMonitor stopped.` | `WinEventMonitor.cs:109` / `:134` | Hook lifecycle |
| `EMERGENCY RELEASE: …` | `GroupManager.cs:397` | Exit/crash release |
| `Saved {n} group(s) to …` | `PersistenceService.cs:114` | state.json write |
| `SPLIT[enter]/[exit]/[replace]/[member-gone]` | `ContainerWindow.xaml.cs` | Split lifecycle transitions |
| `SPLIT[focus] guest=0x…` | `ContainerWindow.xaml.cs` (`FocusSplitMember`) | Focused split member changed (bounded: only on member change) |
| `SHEPHERD[split-foreground]` | `WindowShepherdService.cs` (`SetForeground`) | Split member given real foreground |
| `STATE[transition] winState=… hostRect=…` | `ContainerWindow.xaml.cs` (`StateChanged`) | One line per window-state transition (pre-layout rect diagnostic) |
| `ENV[startup]` / `ENV[launcher]` / `ENV[container]` | `App.xaml.cs` / `ContainerWindow.xaml.cs` | Environment fingerprint (startup, launcher DPI, per-container) |
| `SELFTEST[geometry]` | `App.xaml.cs` (`--selftest-geometry`) | Deterministic partition self-test result |
| `Shepherd capture blocked: DPI-unaware target…` | `WindowShepherdService.cs` (`Capture`) | DPI-unaware guest refused at non-100% scale |

Rules:
- **The log file is held open for the process lifetime** (`FileShare.ReadWrite`, `LoggingService.cs:226`);
  read it with `FileShare.ReadWrite` (as `tests/ValidationDriver/TabDockLog.cs` does) — bare
  `File.ReadAllText` hits a sharing violation.
- **`SHEPHERD[position]` must stay cheap** — no `DescribeWindow` on that line (`WindowShepherdService.cs:209-214`);
  it fires per drag tick, and the `instant-tabswitch` scenario waits on fresh instances.
- **No test may assert on a log line absent from committed source** — the log is instrumentation, not an API.
- `Log()` only enqueues to a bounded queue; lines are dropped (never blocking) if the writer falls behind (`LoggingService.cs:84-113`).

---

## 6. Deeper docs

- `AGENTS.md` — build/publish commands, code style, guarded process-spawn pattern, perf invariants.
- `docs/TESTING.md` — ValidationDriver/GuineaPig harness reference, scenario list, repro techniques.
- `docs/internal/perf-2026-07-25.md` — the `PERF25-NN` pass and its four invariants
  (index resolution, hook gating, cheap `SHEPHERD[position]`, held-open log file).
- `docs/internal/deep-audit-2026-07-17.md` — Shepherd migration rationale (section 6, §6.5:
  backend + crash-recovery journal); `docs/internal/audit-2026-07-25.md` — later audit.
- `KNOWN_ISSUES.md` / `investigation_findings.md` — historical bug-hunt logs (H/M/L-series,
  harness flakes); read before "discovering" anything already documented. `README.md` —
  user-facing manual test checklist and known limitations.
