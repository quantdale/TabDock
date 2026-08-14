# TabDock — Architecture Map

A compact system map for agents and developers working on TabDock: a C#/.NET 8 WPF utility
that merges independent top-level windows into a tabbed container under the **Shepherd**
model — guests are positioned, z-ordered, shown and hidden over the container's content
area, but *never reparented or restyled* (`Services/WindowShepherdService.cs:11-41`). No
third-party NuGet dependencies; all native interop goes through `NativeMethods.cs`. Every
claim below was verified against current source; citations use `path.cs:line`.

---

## Production diagnostics foundation

TabDock's supportability boundary is intentionally read-only and separate from
Shepherd's presentation authority:

```text
BuildIdentity + environment/persistence probes
                    |
             DoctorReportService
                    |
       observed HWND snapshot + bounded trace
                    |
       optional in-process logical presentation snapshot
```

`BuildIdentity` reads generated assembly metadata (`AssemblyInformationalVersion`
and MSBuild `AssemblyMetadata`) so a published executable retains its commit
without Git or a checkout. It does not add a build timestamp; the embedded
commit plus an explicit artifact SHA-256 identifies a distributed build while
preserving deterministic rebuilds. The hash is computed only for explicit
diagnostic commands/export, never on ordinary startup.

`NativeSnapshotService` observes top-level HWNDs, process identity, geometry,
visibility/iconic/zoomed state, foreground and z-order neighbors, monitor/DPI,
DWM cloak status, and fixed `WindowFromPoint` probes. It tolerates destroyed or
inaccessible windows and does not send pointer-bearing cross-process messages.
`ContainerWindow.CreateDiagnosticSnapshot` exposes desired/logical group and
split state without invoking layout, activation, capture/release, or Shepherd
operations. The two models remain distinct so a report can show requested pane
rectangles beside the actual guest rectangles.

`DiagnosticTrace` is a lock-protected 1024-entry ring. Selected foreground,
reorder, move-size, activation, group/split, guest lifecycle, and repair events
carry monotonic sequence numbers and callback/dispatch context where useful.
There is no global `EVENT_OBJECT_LOCATIONCHANGE` subscription and no periodic
health poll. The existing `WindowShepherdService` remains the only native
write authority; diagnostic instrumentation records its existing actions but
does not add a repair loop.

Reports and support ZIPs redact paths under the current user profile, omit raw
window titles, and include only a filtered/sanitized log tail. Nothing is
uploaded automatically. The command-line report can observe the machine and
native TabDock surfaces without an instance; the `Ctrl+Alt+Shift+D` hotkey adds
the richer live logical snapshot when a session is running.

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
`FlushJournalGuarded` (`App.xaml.cs:320-330`) finalizes the synchronous journal
state via `_shepherd.FlushJournal()` (`WindowShepherdService.cs`). Both run on every path:

| Path | What runs |
|------|-----------|
| `Application_Exit` (`App.xaml.cs:167`) | `EmergencyReleaseAll` → `SaveState` → `FlushJournalGuarded` → dispose events/hotkey/mutex/logger (`App.xaml.cs:170-189`) |
| `Application_DispatcherUnhandledException` (`App.xaml.cs:192`) | `SaveStateGuarded` → `FlushJournalGuarded` → `EmergencyReleaseAll` → `Shutdown(1)` (`App.xaml.cs:194-207`) |
| `CurrentDomain_UnhandledException` (`App.xaml.cs:210`) | Terminating: log-only (any thread; runtime is tearing down, `App.xaml.cs:215-221`). Non-terminating: marshals `SaveStateGuarded` + `FlushJournalGuarded` + `EmergencyReleaseAll` to the UI dispatcher with a 1 s deadline (`App.xaml.cs:226-252`) |
| `Application_SessionEnding` (`App.xaml.cs:254`) | One-way/idempotent teardown: save and flush, release guests, stop WinEvent dispatch/retry, normalize containers and persisted layout intent, then call `Shutdown(0)` (`App.xaml.cs:443-507`). If Windows later cancels the original logoff/shutdown request, TabDock still exits deliberately; it never resumes half-torn-down. |
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
- Classifies the target's DPI context and probes the target monitor's effective DPI
  through the contract-correct PMv2 helper (`Services/MonitorDpiService.cs`); a known
  DPI-unaware guest remains accepted because Shepherd placement is in the caller's
  physical screen coordinates. A failed/unknown probe fails closed.
- Snapshots `WINDOWPLACEMENT` (`HasValidPlacement`), bounds, title, executable path,
  PID, GUI thread, process-start time, and a reversible per-capture HWND token.
  The token is removed on release and prevents a same-process recycled HWND from
  becoming a valid delayed callback. DWM transitions are disabled only after the
  durable capture journal is committed (`WindowShepherdService.cs`); logs
  `Shepherd-captured`.

`GroupViewModel.AddCapturedWindow` (`GroupViewModel.cs:168-177`) adds to `Group.Members` and
`Tabs` and activates the new tab; `GroupManager`'s `CollectionChanged` hook maintains the O(1)
HWND→member index (`GroupManager.cs:184-292`) and raises `MonitoringNeededChanged`, which
installs the hooks immediately (`App.xaml.cs:348-361`).

The standalone picker preserves the selected destination across a refresh. A
picker-created group is provisional until at least one selected window is
admitted; if every capture fails, `App.ShowCapturePickerCore` closes and removes
that empty shell. This prevents failed or stale picker actions from creating
zero-tab groups.

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
  `BeginDeferWindowPos`/`DeferWindowPos`/`EndDeferWindowPos`, chaining every
  returned `HDWP` and calling `EndDeferWindowPos` only for a still-valid batch;
  a failed batch falls back per guest) so the panes never visibly separate
  mid-write. The container is paired below the partner
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
`PersistenceService.Save` also omits any fresh group with neither live members
nor persisted tab metadata, and `RestoreGroups` skips legacy zero-tab records;
restored groups with persisted tab metadata remain open as intentional layout
placeholders until the user repopulates or deletes them.

### Pop-out paths

- Tab-strip drag leaving the container bounds (`ContainerWindow.xaml.cs:1014-1026`).
- Dragging or resizing the guest by its own real title bar/edge is always
  re-glued to its assigned pane (`NoteGuestMoveSize`; events from
  `OnGuestMoveSize`). Native movement never releases a tab.
- Tab context-menu **Pop out** (`GroupViewModel.cs:243`).

---

### Cross-machine hardening

- **Physical-coordinate Shepherding and DPI-unaware acceptance.** TabDock remains
  PerMonitorV2 and positions every independent top-level guest's outer HWND in
  physical screen coordinates. A known DPI-unaware guest is accepted at any
  valid monitor DPI; Windows may bitmap-scale its content, so it can look blurry
  exactly as it does standalone, while its outer rect follows the physical pane.
  The guest's 96-DPI logical minimum-track result is converted centrally using
  the target monitor's effective DPI. Unknown or failed guest-awareness/monitor
  probes fail closed. System-aware guests are expected to track correctly on a
  single-DPI system; physical mixed-DPI qualification remains external.
- **Contract-correct arbitrary-monitor DPI probing.** Microsoft documents
  [`GetDpiForMonitor`](https://learn.microsoft.com/en-us/windows/win32/api/shellscalingapi/nf-shellscalingapi-getdpiformonitor)
  as not DPI-aware and says not to call it from a per-monitor-aware thread, so
  production no longer calls it. `NativeMonitorDpiProbe` temporarily sets the
  calling thread to PMv2, verifies that context, creates a hidden top-level PMv2
  helper HWND at the target monitor, and reads `GetDpiForWindow(helper)`. This
  avoids the 96-DPI result that [`GetDpiForWindow(target)`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getdpiforwindow)
  can return for an unaware guest. The helper is destroyed and the original
  thread context restored in a `finally`; a zero result is unavailable and is
  rejected by capture and treated as an unavailable/no-scale result by the
  min-track conversion.
- **Two-tier native window identity.** Hot layout paths check `IsWindow`, PID,
  GUI thread, class, the live `CapturedWindow` binding, and the zero-allocation
  HWND token. Slow/destructive/delayed paths add executable path and native
  `GetProcessTimes` process-start identity. The gate returns explicit
  `Match`, `Mismatch`, or `Unverifiable` outcomes: only `Match` permits native
  mutation, while uncertainty preserves recovery evidence. Crash rescue
  requires/verifies/removes the journaled HWND token; historical tokenless
  journals are retained as named manual-recovery evidence rather than
  discarded or auto-mutated. This distinguishes PID reuse and same-process
  HWND recycling without managed `Process` allocation on layout ticks.
  The hot tier is limited to `PositionAndShow`, `PositionGuest`,
  `PositionGuestsDeferred`, and z-order re-glue. The strong tier is used for
  `Hide`, `Release`, `BringToFront`/foreground handoff, delayed minimized
  restore, dirty min-track probing, lifecycle teardown, and crash recovery —
  every path that can hide, restore, release, or otherwise make a delayed
  native mutation against an external window.
- **Mutation-boundary revalidation.** The durable `JournalCapture`/`JournalHide`
  writes remain before presentation mutation. Capture performs a strong
  pre-token recheck immediately before `SetProp`, then a cheap token/binding
  check immediately before DWM suppression. Hide performs a cheap generation
  check after `JournalHide` and immediately before `ShowWindow(SW_HIDE)`.
  Release and crash rescue repeat the cheap generation check before each later
  placement, visibility, DWM, foreground, and exact-token-removal operation;
  a mismatch stops the transaction and an unverifiable result retains recovery
  evidence. The final check-to-native-call interval is an unavoidable residual
  Win32 race, not an atomicity guarantee. No per-frame path adds executable or
  process-start probes.
- **Monitor-specific maximize bounds** — `WM_GETMINMAXINFO` uses the work area
  of the monitor containing the container. The WPF container has no independent
  primary-monitor `MaxWidth`/`MaxHeight` clamp, so a larger secondary monitor
  is not clipped before the native result is applied.
- **Bounded guest min-track probing** — `SendMessageTimeout` is limited to
  100 ms per guest. `WindowShepherdService` keeps an identity-scoped last-known
  result and falls back to that value (or an unconstrained result) on timeout;
  probes run only when the container's constraint is dirty. The synthetic
  `WM_GETMINMAXINFO` buffer is initialized before dispatch, matching the buffer
  contract of a real system message; the poisoned-buffer self-test prevents an
  indeterminate native minimum from expanding a maximized container. A
  deliberately non-pumping GuineaPig scenario protects the dispatcher bound.
- **Environment fingerprint** (`Services/EnvironmentFingerprint.cs`): one
  `ENV[startup]` line (OS version/build, .NET runtime, bitness, full monitor
  table with bounds/work/primary/effective DPI via `EnumDisplayMonitors` and the
  PMv2 helper), one
  `ENV[launcher]` line (system DPI once the launcher exists), one
  `ENV[container]` line per open container (rects, window state, active
  monitor, DPI, guest), and the `STATE[settled]` snapshot carries the platform
  and guest executable. Bounded — never per-frame — so customer logs are
  self-describing.
- **Deterministic partition and identity/DPI self-tests**: `TabDock.exe
  --selftest-geometry` runs the partition matrix; `--selftest-diagnostics`
  covers post-state `ShowWindow` semantics, the two identity tiers including
  same-process HWND recycling, recovery identity, and the injectable monitor-DPI
  conversion seam.

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
- **Healthy monitoring is an admission invariant** — hook installation is a
  bounded three-attempt transaction. Capture is disabled while hooks are
  unhealthy; if retries are exhausted after guests are already captured, TabDock
  releases and normalizes them, persists layout intent, shows a warning, and
  remains capture-disabled until restart. It never silently leaves guests in a
  degraded lifecycle mode.

---

## 4. Persistence & journal

Two distinct files in `%APPDATA%\TabDock\`:

### `state.json` — layout intent (metadata only)

`PersistenceService` (`PersistenceService.cs`): group id/name/accent/active index plus
per-tab exe path, title, custom label, bounds, and `WasMaximized`. **No HWNDs,
app content, credentials, or live window handles are persisted** — restored
groups start empty as layout intent. The schema is version 2: version 1 is
migrated in memory, while a future/unsupported version is preserved and blocks
later saves rather than silently dropping fields.

Saves are atomic and durable: an existing primary is copied to `.bak`, JSON is
written to `.tmp` with write-through flushing, then atomically replaced. Proven
corruption is quarantined before a valid backup is accepted. Missing primary
plus valid backup is recoverable; unreadable/access-denied, directory-shaped,
and future-version primaries are preserved and never treated as empty. The
service blocks later overwrites after an unreadable/unsupported load. Discrete
semantic mutations use an immediate durable save; high-frequency drag/reorder
intermediate changes remain debounced, with an explicit commit at drag end.
Fresh empty group shells are session-only and are not serialized; valid legacy
zero-tab records are ignored on restore so they cannot accumulate containers
across launches.

### `hidden-windows.json` — crash-recovery journal

`WindowShepherdService` (`WindowShepherdService.cs`). Entries are a versioned
capture-session record containing HWND, PID, GUI-thread identity, executable
identity, window class, process-start identity, a per-capture HWND-generation
token, original visibility, full `WINDOWPLACEMENT`/show state, and the original
DWM transition-suppression state. Current schema v3 is distinct from the
historical v1 minimal journal and v2 full presentation/process-start journal.
Ordering rules:

- **Journal before dangerous mutation** — capture writes the complete original
  state synchronously with write-through flushing before DWM suppression,
  positioning, or hiding. Every later mutable presentation transition updates
  the durable session record synchronously, so a force-kill cannot leave an
  unjournaled state transition.
- **Intentional self-hide is distinct** — a guest-initiated tray-style hide
  receives a durable `DoNotRescue` marker and is not resurrected on the next
  launch. Failed journal writes fail closed and do not hide the guest.
- **Release clears only after restore** — Shepherd verifies the complete
  identity before touching the guest, restores placement, show state,
  visibility, and DWM transition state first, and removes the entry only after
  the required native operations succeed. A positive mismatch may clear only
  the exact old identity tuple; an unverifiable probe retains the journal and
  logical member for retry. `FlushJournal` is a synchronous finalization
  guard, not a debounce timer.
- **`RescueOrphanedWindows`** validates HWND, PID, executable path, class, and
  process-start identity before touching a window. It restores the full recorded
  presentation state, verifies visibility where applicable, and retains failed
  identity-valid entries for retry. Recycled HWNDs are never touched; stale or
  corrupt journal evidence is quarantined/preserved. Legacy v1/v2 entries and
  incomplete v3 entries are written to a durable `hidden-windows.json.pending*`
  sidecar with a manual-recovery diagnostic; they are never silently deleted.

If durable journal storage is unavailable, startup warns the user and capture
is disabled. Logging can fall back to a bounded in-memory tail and layout
persistence can be disabled, but TabDock never hides a guest without durable
crash-recovery protection.

---

## 5. Log-line index

`%APPDATA%\TabDock\logs\TabDock.log`, 1 MB rotation (`LoggingService.cs:10,31`). Lines that
tests and agents may rely on:

If the log directory cannot be created, `LoggingService` keeps a bounded
memory-only tail and startup shows a storage warning. This fallback never
weakens the separate crash-journal gate: capture remains disabled unless the
journal can be durably written.

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
| `Shepherd capture blocked: …dpi::probe-failed…` | `WindowShepherdService.cs` (`Capture`) | Guest awareness/target-monitor DPI probe failed closed |

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
