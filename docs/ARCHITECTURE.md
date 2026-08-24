# TabDock — Architecture Map

A compact system map for agents and developers working on TabDock: a C#/.NET 8 WPF utility
that merges independent top-level windows into a tabbed container under the **Shepherd**
model — guests are positioned, z-ordered, shown and hidden over the container's content
area, but *never reparented or restyled* (`Services/WindowShepherdService.cs`). The
only added package is the stable Microsoft-maintained
`System.Threading.AccessControl` ACL surface used by the product mutation
lease; no unrelated third-party NuGet dependencies are used, and all native
interop goes through `NativeMethods.cs`. Every
claim below was verified against current source; citations are symbol-level
(file plus class/method where useful) rather than brittle line numbers.

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

`App` is the orchestrator (`App.xaml`). Its constructor creates `LoggingService` and
attaches `AppDomain.UnhandledException` *before* `InitializeComponent()` so the earliest
failures are still logged (`App.xaml`). `Application_Startup` (`App.xaml`):

1. **Product-mutation lease** `Global\TabDock-<current-user-SID>` — normal
   TabDock and supervised `--recover-pending` are mutually exclusive across
   that user's sessions, while independent Windows users retain independent
   leases. A second same-user mutating owner exits cleanly without touching
   shared state (`Services/ProductMutationLease.cs`, `AcquireSingleInstanceMutex`
   in `App.xaml.cs`). Read-only diagnostics do not acquire the lease. SID
   discovery or ACL/security verification failure is fail-closed. The lease
   is created/opened through `MutexAcl` with a protected DACL owned by the
   current SID and only `Synchronize | Modify | ReadPermissions`; it grants no
   Everyone/World/inherited/unrelated-user access. The `Global` namespace is
   intentional for same-user cross-session coordination, and an unexpected or
   denied pre-existing object is not weakened or replaced.
2. **`CleanupStaleTempFiles`** — removes orphaned `state.json.tmp` / `hidden-windows.json.tmp`
   from a prior crashed atomic write (`App.xaml`, `App.xaml`).
3. **Service construction** (`App.xaml`): `IconService`, `WindowShepherdService`,
   `PersistenceService`, `GroupManager(shepherd, persistence, log)`, `WinEventMonitor(...)`, `HotkeyService`.
4. **`GuestLifecycleService.Attach(_events)`** — all WinEvent policy behind one call (`App.xaml`, `GuestLifecycleService`).
5. **`MonitoringNeededChanged += SyncWinEventMonitor`** — the hooks gate (`App.xaml`).
6. **`WindowShepherdService.RescueOrphanedWindows(_log)`** — journal replay before any groups
   open (`App.xaml`, `WindowShepherdService`).
7. **`_groups.RestoreState()`** — loads `state.json` (`App.xaml`, `GroupManager`).
8. **Main window + hotkey** — `MainViewModel` wiring, `MainWindow.Show()`, `_hotkey.Register()`
   (`MOD_CONTROL|MOD_ALT|MOD_NOREPEAT`, `HotkeyService`); `HotkeyPressed` → picker (`App.xaml`).
9. **Restored groups get containers** — each `OpenContainer(group)`; a failure is logged and
   skipped, never fatal (`App.xaml`).
10. **`SyncWinEventMonitor()`** — no-op until the first capture; `TabDock startup complete.` (`App.xaml`).

### Shutdown / emergency-release paths

`EmergencyReleaseAll` (`GroupManager`) is the single release-all primitive;
`FlushJournalGuarded` (`App.xaml`) finalizes the synchronous journal
state via `_shepherd.FlushJournal()` (`WindowShepherdService.cs`). Both run on every path:

| Path | What runs |
| ------ | ----------- |
| `Application_Exit` (`App.xaml`) | `EmergencyReleaseAll` → `SaveState` → `FlushJournalGuarded` → dispose events/hotkey/mutex/logger (`App.xaml`) |
| `Application_DispatcherUnhandledException` (`App.xaml`) | `SaveStateGuarded` → `FlushJournalGuarded` → `EmergencyReleaseAll` → `Shutdown(1)` (`App.xaml`) |
| `CurrentDomain_UnhandledException` (`App.xaml`) | Terminating: log-only (any thread; runtime is tearing down, `App.xaml`). Non-terminating: marshals `SaveStateGuarded` + `FlushJournalGuarded` + `EmergencyReleaseAll` to the UI dispatcher with a 1 s deadline (`App.xaml`) |
| `Application_SessionEnding` (`App.xaml`) | One-way/idempotent teardown: save and flush, release guests, stop WinEvent dispatch/retry, normalize containers and persisted layout intent, then call `Shutdown(0)` (`App.xaml`). If Windows later cancels the original logoff/shutdown request, TabDock still exits deliberately; it never resumes half-torn-down. |
| `Application_Startup` catch (`App.xaml`) | Same trio + `Shutdown(1)` (`App.xaml`) |

`FlushJournalGuarded` runs from every shutdown/crash path listed above
(`App.xaml.cs`).
`ContainerWindow.IsAppShuttingDown` is set before every shutdown so `Closing` skips its
Yes/No prompt (`App.xaml`, `ContainerWindow.xaml`).

### Product trust and interaction projections

The launcher has three read/diagnostic projections around existing authorities:

- `PendingRecoveryService.GetLauncherAttention` reuses the canonical pending-file
  parser and unresolved-evidence calculation. It skips temporary-fragment cleanup,
  takes no recovery lease, and performs no native recovery mutation. The launcher
  banner counts unresolved evidence files, treats unreadable/corrupt evidence as
  attention, and disappears only when discovery is clear. It presents the exact
  supported commands `TabDock.exe --pending-recovery` and
  `TabDock.exe --recover-pending`; typed confirmation and supervised recovery stay
  in the existing command path.
- `GroupManager.CaptureAdmission` is the only capture-admission authority. Its
  allowed/reason record and `CaptureAdmissionChanged` event project to the launcher,
  container Add window surface, and capture picker. Admission failure is distinct
  from a missing global shortcut: capture controls are blocked with the canonical
  reason, while local/button fallbacks remain available when only a shortcut is
  unavailable. WinEvent retry transitions update these surfaces without restart.
- `HotkeyService` registers `Ctrl+Alt+PageUp` and `Ctrl+Alt+PageDown` with
  `MOD_NOREPEAT`. `App.OnTabNavigationHotkeyPressed` proves the foreground HWND is
  a current captured guest or the owning container/chrome, resolves its live group,
  and invokes `ContainerWindow.NavigateTabs`. Unrelated foreground applications,
  stale/recycled HWNDs, closed containers, and active modal/chrome interactions are
  strict no-ops. Local Ctrl+Tab uses the same container operation and
  `TabNavigationPolicy`; `Ctrl+Alt+Left/Right` is deliberately not registered
  because common graphics/display-driver shortcuts collide with those combinations.

The container's persistent `Split ▾` button is an always-visible UI affordance,
not a second split state machine. `SplitAffordancePolicy` projects eligible partner,
presented focus, dormant resume/show, and end actions from current
`CapturedWindow` references. Every action is revalidated against the live
`SplitPresentationController` state before routing to the existing
`StartSplitFrom`, `FocusSplitMember`, `ResumeSplitPair`, or `ExitSplit` paths.
This preserves LEFT/RIGHT identity, dormant relationships, composite `DisplayTabs`
presentation, member-removal semantics, and Shepherd's independent top-level guest
windows.

---

## 2. Capture lifecycle

### Capture

Trigger: the `Ctrl+Alt+G` hotkey (`HotkeyService`) or a container's "Add window" button →
`ShowCapturePicker` (`App.xaml`; re-entrancy-guarded via `_pickerOpen` and the
close-prompt check, `App.xaml`). The picker enumerates top-level windows with
cheapest-first filters: visible → not tool-window → titled → not own window
(`GroupManager.IsOwnWindow`, `GroupManager`) → not already captured
(`GroupManager`) → not DWM-cloaked (`CapturePickerViewModel`).

The picker row's continuity key is a UI-only projection, not a second native
authority: HWND, PID, GUI thread, process-start ticks, class name, and the
case-insensitive Windows executable path must all match before a checked row is
restored after Refresh. Rows whose process identity cannot be read are omitted
from production enumeration. The final handoff reuses
`WindowIdentityGate.EvaluateBeforeCaptureToken`, and Shepherd performs its
authoritative admission transaction again immediately before installing the
capture token. Titles remain mutable display metadata and are intentionally not
part of the identity key.

On OK, `App` resolves/creates the target group + container, then calls
`container.CaptureWindow(hwnd)` (`ContainerWindow.xaml`), which re-checks the
no-nesting and already-captured rules, then `_shepherd.Capture(hwnd, out error)`
(`WindowShepherdService`):

- Refuses dead HWNDs and own-process windows (`WindowShepherdService`).
- **Elevation check fails closed** — indeterminate target elevation + non-elevated TabDock
  refuses the capture (`WindowShepherdService`).
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

`GroupViewModel.AddCapturedWindow` (`GroupViewModel`) adds to `Group.Members` and
`Tabs` and activates the new tab; `GroupManager`'s `CollectionChanged` hook maintains the O(1)
HWND→member index (`GroupManager`) and raises `MonitoringNeededChanged`, which
installs the hooks immediately (`App.xaml`).

The standalone picker preserves the selected destination across a refresh. A
picker-created group is provisional until at least one selected window is
admitted; if every capture fails, `App.ShowCapturePickerCore` closes and removes
that empty shell. This prevents failed or stale picker actions from creating
zero-tab groups.

### Tab switch

`GroupViewModel.SetActiveTab` (`GroupViewModel`) → `GroupManager.SwitchActiveTab`
(`GroupManager`, logs `Switched group ...`) → `ActiveTab` change →
`ContainerWindow.SyncShepherdActiveWindow` (`ContainerWindow.xaml`): **position/show
the new guest first** (it covers the outgoing one), then `_shepherd.Hide(old)` if still a
member (`WindowShepherdService` — journals *before* hiding). Positioning runs
`LayoutShepherdActiveWindow` (`ContainerWindow.xaml`): content rect off the
`NativeHwndHost` marker (`GetContentAreaScreenRect`, `ContainerWindow.xaml`),
skip redundant re-glues when the guest already covers it within 1 px, then `PositionAndShow`
(`WindowShepherdService`): `SW_RESTORE` if iconic/zoomed, `SetWindowPos`
`HWND_TOP` + `SWP_SHOWWINDOW`, `PairZOrderBehind(container, guest)`
(`WindowShepherdService`), `JournalClear`, `SHEPHERD[position]`.

### Movement synchronization

Container move/resize re-glues the guest(s) to the content marker. Every
trigger — the native `WM_WINDOWPOSCHANGED` (hooked in `WndProc` so the guest is
re-glued in the same native message flow as the container's own movement),
`LocationChanged`, `SizeChanged`, and the post-layout `LayoutUpdated` — funnels
into one coalesced `RequestRelayout()` (a pending flag + a single
Render-priority dispatcher callback owned by `PresentationLayoutCoordinator`),
so at most one batch of native reposition calls is issued per WPF frame instead
of 3-5, and the batch always runs after WPF has arranged the content marker to
its final rect. This removed the per-frame double-move/backward-jump that made
dragging look glitchy. Jitter hardening: `RequestRelayout` latches
`ensureFinalPass` even when a Render is already pending (so the
`WM_EXITSIZEMOVE` final z-order pass survives a queued frame), the Render
callback clears `pending` BEFORE `execute` so a mid-callback re-queue lands on
the next frame, stale settle callbacks are generation-gated by the split
controller's own settle generation, a dormant
pair never receives a split relayout, and pane-refusal state is only cleared
on a constraint change — not per frame — so a refused pane is not retried until
the constraint actually changed. Queued relayout frames are never discarded:
there is no invalidation transition, so each queued callback executes exactly
once against the presentation state current at callback time (Wave 3D Model B;
the former unreachable layout-generation discard machinery was removed).

### Vertical split screen (two guests)

From a captured tab's context menu, TabDock can display exactly two guests
simultaneously in a LEFT/RIGHT vertical split (the Shepherd model is unchanged —
both stay independent top-level HWNDs, never reparented/restyled). Production
transitions are governed by `SplitPresentationPolicy` through
`SplitPresentationController` with commit-on-success semantics: the controller
delegates every decision (pair identity, presented/dormant, foreground,
generation, settle) to the pure policy, and presentation state commits only
after the corresponding native transition succeeds.
`PresentationLayoutCoordinator` owns coalesced relayout scheduling and
redundant-frame suppression; `ContainerWindow` still owns WPF wiring (WndProc, chrome,
timers, hit-testing) but delegates policy/layout decisions to these controllers
for testability, clear ownership, and jitter hardening.
`SplitInteractionPolicy` is a pure hit-test → `SplitInteractionAction`
classifier that makes non-member activation deterministic in hosted CI (a
handled preview event still suspends the pair; button, right-click/hover, and
stale-identity hits are correctly filtered). Native transition outcomes are
asserted through the controller's recording presentation-operations seam
(`IPresentationOperations`) in unit tests; the former CI-only budget-counter
scaffolding was removed as dead (Wave 1).

The container caption's Workspace menu switches to an already-open group or creates
one in the existing shell. The launcher is hidden while a container is open and
remains only as the no-group/global-hotkey fallback. Routine Add window capture is
an in-window panel; the standalone picker remains for the fallback path.

- **State** — `SplitPresentationController.Left` / `.Right` hold
  `CapturedWindow` references (identity, not index, so the pair survives tab
  reordering); `.Foreground` tracks which member is z-order-top and
  `.IsPresented` separately records whether the relationship currently occupies
  the two panes. Split is runtime-only (not persisted), and a dormant
  relationship may coexist with a non-member full-width active guest. The
  controller is the sole runtime split authority.
- **Composite tab (presentation only)** — the strip renders the relationship as
  ONE visual item `[ A | B ]` (a subtle central separator) instead of two
  unrelated tabs: `GroupViewModel.DisplayTabs` is the strip projection
  (`SplitCompositeViewModel` wraps the two member `TabViewModel`s; the RIGHT
  member's ordinary tab is suppressed while the relationship exists; the
  composite occupies the LEFT member's visual position; explicit exit or
  structural invalidation restores ordinary tabs in `Group.Members` order).
  Domain identity is never merged — the two `CapturedWindow`s remain separate
  members. The projection mirrors `Tabs`
  exactly when no split relationship is defined (Add/Remove/Move mirrored so ListBox
  containers survive reorders).
- **Enter/exit** — `EnterSplit(left, right)` / `ExitSplit(keepActive)` route
  through both the existing tab context menu and the persistent `Split ▾`
  affordance (`SplitAffordancePolicy`: disabled below two tabs, eligible
  partner actions, presented focus/end actions, and dormant resume/show/end
  actions). The context menu still offers a direct action at exactly two tabs,
  a partner submenu at three+, and `Exit split screen` when the relationship
  exists. Clicking a composite HALF keeps the pair and
  focuses that member without hiding the partner. An ordinary left-click on a
  NON-paired tab journal-safely suspends the presented pair, keeps the
  relationship/composite, and presents the selected guest full-width; clicking
  either composite half resumes the unchanged LEFT/RIGHT pair. Split ends ONLY
  from an explicit `Exit split screen` / explicit Split Screen replacement, or
  structural member removal (pop-out, ×, middle-click, self-close/hide, group
  deletion). Ctrl+Tab follows the current presentation: it is pair-scoped when
  the pair is presented and ordinary single-guest navigation when dormant.
  Per-half × / middle-click pop THAT member out; right-click builds a
  member-specific menu (Pop out / Close window plus `Exit split screen`; an
  existing relationship member does not receive a redundant `Split screen`
  action in either presented or dormant state). Composite dragging is disabled
  while the relationship exists (the composite is not a drag unit;
  normal-mode drag reorder is unchanged). Departing members are
  hidden via `_shepherd.Hide` (journal-before-hide preserved); neither member
  is ever released by a split transition.
- **Layout** — `LayoutSplitPanes` derives LEFT/RIGHT halves from the content
  rect via `SplitGeometry.Partition` (`leftW = Width/2`, right gets the
  remainder; no DPI conversion; the single deterministic definition, qualified
  by the headless xUnit geometry suite) and establishes the local order
  `foreground guest → partner guest → container`. The 1px redundant-glue guard
  is per-pane; when a re-glue is needed both panes plus the container pin are
  written in ONE compositor transaction (`PositionGuestsDeferred`:
  `BeginDeferWindowPos`/`DeferWindowPos`/`EndDeferWindowPos`, chaining every
  returned `HDWP` and validating each guest immediately before it is queued;
  a failed native Defer follows Win32's no-End path, while a stale generation
  closes a valid batch and skips the stale-guest fallback) so the panes never
  visibly separate
  mid-write. The container is paired below the partner
  without being pushed behind unrelated desktop windows. When both panes are
  already glued, the cheap pin runs ONLY after verifying the pair's actual
  RELATIVE order (`ZOrder.IsOrderedAbove(top, bottom)` — an upward
  `GW_HWNDNEXT` walk that succeeds when the top member sits anywhere above the
  bottom member, ignoring IME/accessibility/overlay helper HWNDs), not strict
  adjacency: helper HWNDs may legally interleave, and fighting them for exact
  neighbors would churn a repair per relayout pass. A strip-initiated focus
  switch (which raises no guest natively) would otherwise wedge the container
  between the panes and occlude the just-focused member.

  Win32 does not provide an atomic operation that combines the per-guest
  identity validation with the later `EndDeferWindowPos` commit. A guest can
  therefore be destroyed or recycled in the small check-to-commit interval;
  the implementation treats that as a bounded residual race, closes/abandons
  the batch on a known validation failure, and falls back only for native
  Begin/Defer/End failure. It does not claim impossible atomic HWND identity.
- **Focused member (one canonical operation)** — `FocusSplitMember(member)`
  is the ONLY path that focuses or resumes a member of the defined pair: it updates
  `_splitForeground` (z-top) and `_shepherdActiveWindow` (logical active +
  tab highlight + `Group.ActiveIndex`), emits the bounded `SPLIT[focus]`
  diagnostic (only when the focused member changes), re-glues both panes with
  the member on top, and grants the member real foreground. Every entry point
  routes through it: composite half click, active-tab sync, direct guest click
  (WinEvent), and the WM_ACTIVATE reassert — so LEFT and RIGHT are peers after
  split creation and no initiator/partner asymmetry can arise. From dormant
  state it first resumes both panes; it never changes split membership or
  reverses LEFT/RIGHT identity.
- **Survivor promotion** — when a member leaves a presented pair the split ends
  and the survivor is promoted to the single visible guest; when a dormant pair
  loses a member, the current non-member guest remains visible and the surviving
  former member remains hidden as an ordinary tab. `ReleaseTab` honors either
  transition (no positional neighbour pick may hide or displace the selected
  guest afterwards).
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
  reassert. Guest hide classification is unified in the `GuestHideProvenance`
  ledger (`Services/GuestHideProvenance.cs`): every expected-hide operation is
  recorded there bound to its per-capture token, and
  `GuestLifecycleService.OnWindowHidden` consults that ledger instead of
  active-tab inference, suspension flags, or container-minimize expectation
  maps to distinguish intentional presentation hides from guest-initiated
  teardown. A split member leaving
  the group (pop-out, drag-out, self-close, self-hide) ends the relationship;
  presented-pair removal promotes the survivor, while dormant-pair removal
  retains the current non-member guest.
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
it). The destructive Yes path of that dialog is additionally nonce-guarded:
releasing the group requires a one-shot released-close nonce bound to the
exact HWND instance, so a stale or recycled container cannot drive a
destructive release.

Log vocabulary: `SPLIT[enter]`, `SPLIT[suspend]`, `SPLIT[single]`,
`SPLIT[resume]`, `SPLIT[settled]`, `SPLIT[member-gone]`, `SPLIT[exit]`, plus
`SPLIT[persist]` (a newly-visible non-member hidden to preserve the pair's
visible set) and the bounded `SPLIT[focus]`. `SPLIT[replace]` is NOT emitted
anywhere.

### Release chain

Production workflows are pinned to immutable SHAs. The repository is
**main-only**: `main` is the sole development/integration branch and is
qualified directly on push by `build.yml` (exact-SHA hosted-CI gates).
There is no `agent/staging` branch and no `promote-staging` workflow;
future agents must develop, commit, and push against `main`. The two-stage
release chain (`prepare-release-candidate.yml` → `publish-release.yml`) remains
exact-SHA and immutable as described in `README.md` and
`docs/release/publication-gates.md`.

### Release (tab) removes the member from `Group.Members`

first (the index drops it via `CollectionChanged`), then `_shepherd.Release(cw, show)`
(`WindowShepherdService`):

- Window already gone → `JournalClear` + log (`WindowShepherdService`).
- `show:false` (guest-initiated hide): `JournalClear(immediate: true)` **before** `SW_HIDE`,
  transitions re-enabled (`WindowShepherdService`).
- `!HasValidPlacement`: bounds fallback — `SetWindowPos(OriginalBounds)` + `SW_SHOW` +
  `SetForegroundWindow` (`WindowShepherdService`).
- Normal: `SetWindowPlacement(OriginalPlacement)` (falls back to bounds `SetWindowPos` on
  failure), `ShowWindow(showCmd)`, `SetForegroundWindow`, `JournalClear`
  (`WindowShepherdService`). The `WINDOWPLACEMENT` buffer is the
  44-byte layout modern Windows 10/11 user32 enforces (`length` must be 44;
  the SDK header's trailing `rcDevice` is never populated and passing 60 fails
  `SetWindowPlacement` with `ERROR_INVALID_PARAMETER`) — see
  `NativeMethods.WINDOWPLACEMENT` and `NativeInteropSelfTest`.

Release-by-reference for WinEvent teardown: `GroupManager.ReleaseMember` (`GroupManager`)
via `RemoveDeadMember` (`GuestLifecycleService`), which prefers
`container.ReleaseCapturedWindow` (`ContainerWindow.xaml` → `GroupViewModel.ReleaseTab`,
which keeps the active tab active, `GroupViewModel`).

### Empty-group container close

`RemoveDeadMember` closes the container when `Members.Count == 0` and removes the group only
if `PersistedTabs.Count == 0` (a restored group carries saved layout intent)
(`GuestLifecycleService`). Popping out the last tab triggers `EmptiedByPopOut` →
container `Close` (`ContainerWindow.xaml`, `GroupViewModel`);
`App.OnContainerClosed` removes only empty, never-repopulated groups (`App.xaml`).
`PersistenceService.Save` also omits any fresh group with neither live members
nor persisted tab metadata, and `RestoreGroups` skips legacy zero-tab records;
restored groups with persisted tab metadata remain open as intentional layout
placeholders until the user repopulates or deletes them.

### Pop-out paths

- Tab-strip drag leaving the container bounds (`ContainerWindow.xaml`).
- Dragging or resizing the guest by its own real title bar/edge is always
  re-glued to its assigned pane (`NoteGuestMoveSize`; events from
  `OnGuestMoveSize`). Native movement never releases a tab.
- Tab context-menu **Pop out** (`GroupViewModel`).

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
- **DWM transition suppression is retained by design.** Production calls
  `DwmSetWindowAttribute(DWMWA_TRANSITIONS_FORCEDISABLED)` in exactly three
  places: capture (after the durable journal commit and the immediate
  pre-mutation identity recheck), normal release, and crash rescue. Its value
  is real: guests are hidden/shown continuously during tab switching, and
  without suppression every switch triggers DWM minimize/restore animations
  that look like broken rendering. The original attribute value is read before
  mutation, journaled, and restored exactly on release and on rescue after
  identity revalidation; an unverifiable or recycled HWND refuses the restore
  and preserves the journal. The only residual hard-kill behavior is bounded
  and cosmetic: if TabDock is terminated abruptly after capture, the guest
  keeps transitions disabled until the next TabDock launch performs journaled
  rescue. The legacy audit branch removed this mutation entirely; the current
  reversible, journal-backed design is kept because the benefit is documented
  and the failure mode is a bounded recoverable cosmetic state.
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
- **Deterministic partition qualification**: the exhaustive partition matrix,
  seeded fuzz, and size-constraint math live in the headless xUnit suite
  (`tests/UnitTests/GeometryTests.cs`). The product executable retains only the
  `--selftest-native-abi` probe, which produces real-user32 `WINDOWPLACEMENT`
  ABI evidence (44-byte contract, get/set round trip) plus a per-machine
  environment report for the compatibility matrix.

## 3. WinEvent pipeline: event → handler → effect

Hooks installed in `WinEventMonitor.Start` (`WinEventMonitor`): `EVENT_OBJECT_DESTROY`,
`EVENT_SYSTEM_FOREGROUND`, `EVENT_OBJECT_REORDER`, `EVENT_OBJECT_NAMECHANGE`, `EVENT_SYSTEM_MINIMIZESTART`,
`EVENT_OBJECT_HIDE`, and one ranged `EVENT_SYSTEM_MOVESIZESTART..END` hook. A partial install
unwinds and reports failure (`WinEventMonitor`).

The native callback filters `idObject/idChild != 0` and zero HWNDs, then the **direct-HWND-match**
captured-index resolve (`GetCapturedWindow` — one dictionary probe; a null result is the complete
membership proof) — never `GetAncestor`, useless under Shepherd (guests are their own
root) and invalid for already-destroyed windows (`WinEventMonitor`). Survivors are
dispatched via **`SynchronizationContext.Post` — never `Send`** (handlers must observe the UI
state *after* the causing operation; `WinEventMonitor`). `Raise` re-verifies
`_running` and re-resolves the HWND against the index by reference so a recycled handle never
receives a stale member's queued event, then switches on event type (`WinEventMonitor`).
The desktop `EVENT_OBJECT_REORDER` path is the one deliberate exception to
direct guest-HWND filtering: Windows reports the desktop client object
(`GetDesktopWindow`, `OBJID_CLIENT`, `CHILDID_SELF`) for top-level z-order
changes, so the callback snapshots `GetForegroundWindow()` and the UI handler
revalidates that snapshot before pairing a captured guest.

| WinEvent | Handler (`GuestLifecycleService.Attach`, `GuestLifecycleService`) | Effect |
| --- | --- | --- |
| `EVENT_OBJECT_DESTROY` | `OnWindowDestroyed` (`GuestLifecycleService`) | Log `destroyed; removing its tab` → `RemoveDeadMember(show: true)` |
| `EVENT_OBJECT_HIDE` | `OnWindowHidden` (`GuestLifecycleService.cs`) | Hide classification via the unified `GuestHideProvenance` ledger: expected-hide operations are recorded bound to their per-capture tokens, so tab-switch hides, suspension hides, and container-minimize hides are recognized without active-tab inference. An unledgered hide from a live HWND passes → log `hid itself` → `RemoveDeadMember(show: false)` |
| `EVENT_SYSTEM_MINIMIZESTART` | `OnWindowMinimized` (`GuestLifecycleService`) | Log → `container.RestoreMinimizedWindow` — 200 ms deferred, re-checks iconic + visible + still active before `SW_RESTORE` (`ContainerWindow.xaml`) |
| `EVENT_SYSTEM_MOVESIZESTART/END` | `OnGuestMoveSize` (`GuestLifecycleService`) | `container.NoteGuestMoveSize` — end only; native movement/resize is re-glued to the assigned pane and never releases a tab (`ContainerWindow.xaml.cs`) |
| `EVENT_SYSTEM_FOREGROUND` | `OnForegroundChanged` (`GuestLifecycleService`) | `container.PairZOrderBehindGuest` → `shepherd.PairZOrderBehind` re-pins the container behind the guest (`ContainerWindow.xaml`, `WindowShepherdService`) |
| `EVENT_OBJECT_REORDER` (desktop client) | `OnZOrderChanged` (`GuestLifecycleService.cs`) | Callback-time foreground HWND is revalidated on the UI thread; if it is a captured guest, routes through the same `PairZOrderBehindGuest` policy to repair direct-click adjacency |
| `EVENT_OBJECT_NAMECHANGE` | `DebounceNameChanged` (`GuestLifecycleService`) | Per-HWND 250 ms coalescing timer → `HandleNameChanged` (`GuestLifecycleService`): custom label wins, empty titles ignored, unchanged titles skipped, else update `OriginalTitle` + `RefreshTabTitle` |

**Invariants** (see `docs/internal/perf-2026-07-25.md`):

- **Post, never Send** — `WinEventMonitor`.
- **O(1) resolution** — handlers resolve via `GroupManager.TryGetCapturedMember` (`GroupManager`),
  one probe; never scan `Groups`.
- **Hooks gated on `IsMonitoringNeeded`** (`GroupManager`) — `App.SyncWinEventMonitor`
  (`App.xaml`): install immediately on first capture, removal deferred one dispatcher turn.
- **Healthy monitoring is an admission invariant** — hook installation is a
  bounded three-attempt transaction. Capture is disabled while hooks are
  unhealthy; if retries are exhausted after guests are already captured, TabDock
  releases and normalizes them, persists layout intent, shows a warning, and
  remains capture-disabled until restart. It never silently leaves guests in a
  degraded lifecycle mode.

### Native-event replay and measurement boundary

`Services/WinEventRoutingPolicy.cs` is the native-free admission seam for the
hook callback. The callback still performs the live captured-member lookup,
passes the resolved object through the policy, and posts only relevant events.
The posted handler performs a second reference-identity lookup before invoking
guest lifecycle callbacks. That second lookup is intentional HWND-generation
protection: an event queued for a released or recycled handle must not act on a
new member. `Services/NativeInteractionReplay.cs` models this policy boundary
with explicit identities, identity probe results, visibility, foreground, and
native intents/refusals; it does not emulate USER32 or create windows.

The bounded WinEvent measurement suite exercises captured and irrelevant event
storms, child/object filtering, desktop reorder, queued stale dispatch, and
lifecycle callback counts. Its representative result is one callback membership
probe and one dispatch revalidation per relevant captured event; child/irrelevant
events perform zero membership probes. No cross-event HWND cache was accepted:
the dispatch probe is the safety proof against handle reuse, so removing it
would change observable fail-closed behavior rather than merely reduce work.

The physical ValidationDriver has a separate evidence boundary. A
`DesktopQualificationLease` records privacy-safe session/monitor/foreground
state and invalidates permanently on foreign coverage, foreign foreground,
identity change, or unverifiable observations. `TestRunProvenance` supplies the
ownership categories `OWNED_PROCESS`, `OWNED_WINDOW`,
`ADOPTED_EXTERNAL_WINDOW`, `FOREIGN`, and `STALE_RECYCLED`; `OWNED_WINDOW` and
`ADOPTED_EXTERNAL_WINDOW` are the only input-target categories, while only
`OWNED_PROCESS` is eligible for the process kill list. An adopted external
window may be an input target only while its complete stable identity still
matches. `NativeInteractionTimeline`
and the root run manifest link bounded role-based evidence without persisting
titles, URLs, document contents, or arbitrary user paths.

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
  logical member for retry. `JournalClear` is synchronous when an actual entry
  matches and returns without a disk write when none matches; `FlushJournal` is
  a synchronous finalization guard, not a debounce timer.
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

`%APPDATA%\TabDock\logs\TabDock.log`, 1 MB rotation (`LoggingService`). Lines that
tests and agents may rely on:

If the log directory cannot be created, `LoggingService` keeps a bounded
memory-only tail and startup shows a storage warning. This fallback never
weakens the separate crash-journal gate: capture remains disabled unless the
journal can be durably written.

| Log line (substring) | Emitter | Meaning |
| --- | --- | --- |
| `SHEPHERD[position] guest=0x… rect=…` | `WindowShepherdService` | Guest (re)positioned/shown; per mouse tick during container drag |
| `Shepherd-captured 0x…` | `WindowShepherdService` | Capture succeeded (new member) |
| `Shepherd-released 0x…` | `WindowShepherdService` (`Release`) | Release: guest-initiated-hidden / bounds-fallback / normal |
| `SHEPHERD[hide] guest=0x…` | `WindowShepherdService` | Inactive tab hidden |
| `SHEPHERD[bring-to-front]` | `WindowShepherdService` | Foreground re-assert after container activation |
| `SHEPHERD[re-glue]` | `ContainerWindow.xaml.cs` | Native move/size ended outside the assigned pane |
| `SHEPHERD[rescue]` | `WindowShepherdService` (`RescueOrphanedWindows`) | Journal replay at startup |
| `SHEPHERD[position-fail]` | `WindowShepherdService` | First positioning failure per HWND (UIPI/dead HWND) |
| `Switched group {id} to tab {i}` | `GroupManager` | Active-tab change |
| `Reordered tab {old}->{new} in group {id}` | `GroupManager` | Drag reorder committed |
| `Released tab {i} from group {id}` | `GroupManager` | Tab released |
| `… destroyed; removing its tab.` | `GuestLifecycleService` | Destroy teardown |
| `… hid itself (tray-style close); releasing its tab hidden.` | `GuestLifecycleService` | Guest-initiated hide teardown |
| `… minimized; restoring it inside its tab.` | `GuestLifecycleService` | Minimize restore |
| `WinEvent: title changed for 0x…` | `GuestLifecycleService` | Debounced title refresh |
| `WinEventMonitor started (hooks: …)` / `WinEventMonitor stopped.` | `WinEventMonitor` | Hook lifecycle |
| `EMERGENCY RELEASE: …` | `GroupManager` | Exit/crash release |
| `Saved state to …` | `PersistenceService` (`CommitJson`) | state.json write |
| `SPLIT[enter]/[suspend]/[single]/[resume]/[settled]/[member-gone]/[exit]` | `ContainerWindow.xaml.cs` | Split lifecycle transitions (`SPLIT[replace]` is not emitted) |
| `SPLIT[focus] guest=0x…` | `ContainerWindow.xaml.cs` (`FocusSplitMember`) | Focused split member changed (bounded: only on member change) |
| `SHEPHERD[split-foreground]` | `WindowShepherdService.cs` (`SetForeground`) | Split member given real foreground |
| `STATE[transition] winState=… hostRect=…` | `ContainerWindow.xaml.cs` (`StateChanged`) | One line per window-state transition (pre-layout rect diagnostic) |
| `ENV[startup]` / `ENV[launcher]` / `ENV[container]` | `App.xaml.cs` / `ContainerWindow.xaml.cs` | Environment fingerprint (startup, launcher DPI, per-container) |
| `Shepherd capture blocked: …dpi::probe-failed…` | `WindowShepherdService.cs` (`Capture`) | Guest awareness/target-monitor DPI probe failed closed |

Rules:

- **The log file is held open for the process lifetime** (`FileShare.ReadWrite`, `LoggingService`);
  read it with `FileShare.ReadWrite` (as `tests/ValidationDriver/TabDockLog.cs` does) — bare
  `File.ReadAllText` hits a sharing violation.
- **`SHEPHERD[position]` must stay cheap** — no `DescribeWindow` on that line (`WindowShepherdService`);
  it fires per drag tick, and the `instant-tabswitch` scenario waits on fresh instances.
- **No test may assert on a log line absent from committed source** — the log is instrumentation, not an API.
- `Log()` only enqueues to a bounded queue; lines are dropped (never blocking) if the writer falls behind (`LoggingService`).

---

## 6. Deeper docs

- `AGENTS.md` — build/publish commands, code style, guarded process-spawn pattern, perf invariants.
- `docs/TESTING.md` — ValidationDriver/GuineaPig harness reference, scenario list, repro techniques.
- `docs/internal/perf-2026-07-25.md` — the `PERF25-NN` pass and its four invariants
  (index resolution, hook gating, cheap `SHEPHERD[position]`, held-open log file).
- `docs/internal/deep-audit-2026-07-17.md` — Shepherd migration rationale (section 6, §6.5:
  backend + crash-recovery journal); `docs/internal/audit-2026-07-25.md` — later audit.
- `KNOWN_ISSUES.md` / `docs/internal/investigation_findings.md` — historical bug-hunt logs (H/M/L-series,
  harness flakes); read before "discovering" anything already documented. `README.md` —
  user-facing manual test checklist and known limitations.
