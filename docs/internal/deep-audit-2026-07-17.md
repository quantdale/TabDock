# TabDock Deep Audit — 2026-07-17

Full-codebase audit performed in response to the recurring-bug / lag / dead-keyboard-input complaints.
Sources: every main-app source file, git history (17 commits), and ~1.2 MB of runtime logs
(`%APPDATA%\TabDock\logs\TabDock.log` + `.old`, covering 2026-07-11 → 2026-07-17).

---

## 1. Executive summary

**The bugs are not random, and they are not caused by C#, .NET, or WPF.** They are the
predictable consequences of the project's core architectural bet: **reparenting other
processes' top-level windows with cross-process `SetParent`**, then compensating for
everything that breaks (input routing, focus, DPI, rendering, geometry) with an
ever-growing set of counter-mechanisms that fight the OS and each other.

Windows has never supported this composition model well. Cross-process `SetParent`
implicitly fuses the input queues of the two threads (the same effect as
`AttachThreadInput` — Microsoft's own documentation and the Old New Thing blog describe
this as sharing a joint bank account: either party can freeze the other), silently
rewrites the child's DPI-awareness context, and breaks the compositor assumptions of
every modern GPU-rendered app (Chromium, Electron, WinUI, UWP). Each user-visible bug —
black tabs, Edge misalignment, dead keyboard, drifting guests, Notepad self-destructing —
is one head of this hydra. Each fix so far has been a compensating patch at the wrong
altitude, which is exactly why "fixed" bugs keep reappearing: the fixes treat symptoms
whose common cause regenerates them in a new form with every new guest app, DPI
configuration, or Windows update.

**A rewrite in Rust (or any language) would faithfully reproduce every one of these bugs**,
because they live in Win32 windowing semantics, not in the language or UI framework. The
managed/WPF layer is, in fact, the healthiest part of the codebase.

**Recommendation:** keep the C#/WPF shell, and replace the *windowing model*: stop
adopting foreign windows (reparent/strip/attach) and instead *coordinate* them —
an overlay tab strip that positions, shows, hides, and z-orders unmodified top-level
windows. Section 6 details the design; section 7 gives an incremental migration plan
that deletes far more code than it adds.

---

## 2. Evidence that the architecture (not the code quality) is the problem

### 2.1 Git history: the whack-a-mole signature

Of 17 total commits, at least 10 are fixes, re-fixes, or *reverts of previous fixes* for
the same three symptom families (DPI alignment, input/focus, render corruption):

- `d70db05` forward DPI on capture → `836d411` remove synthetic WM_DPICHANGED →
  `9dcaadc` reverse the synthetic WM_DPICHANGED → `dd4bca8` Edge-specific DPI forwarding →
  `7c108bf` **remove** Edge-specific DPI forwarding → `b3d80fa` strip WS_MAXIMIZE for Edge.
- `053a7d1` / `2f2b572` fix H2 drag oscillation (twice), host-background smear, maximize black-out.
- `cd75e9f` / `b8b5b8b` "enhance input handling and focus management" (twice, most recent).

Three mutually incompatible DPI strategies were shipped and rolled back within one week.
That is not sloppy engineering — the code around each is careful and well-commented — it
is the signature of a problem that has no stable solution at this layer.

### 2.2 Runtime logs: the symptoms, recorded live

**(a) The dead-keyboard smoking gun.** The input design intends a *persistent*
`AttachThreadInput` between the WPF UI thread and the active guest's thread
(`Infrastructure/NativeHwndHost.cs:33-37`). The log shows the attachment being destroyed
milliseconds after it is created, by the code's own re-entrant focus handling:

```
17:40:51.992 INPUT[attach]  guest=0x4D162C guestThread=45568 hostThread=87968
17:40:51.994 INPUT[host-wmkillfocus] host=0xCF08D4
17:40:51.996 INPUT[detach]  guestThread=45568 hostThread=87968 detached=True focus=0x0
17:40:51.998 INPUT[focus]   guest=0x4D162C setFocus=0xCF08D4 ...
```

Sequence: `AttachActiveGuest` attaches the queues and calls `SetFocus(guest)`
(`NativeHwndHost.cs:130-137,201`). `SetFocus` synchronously delivers `WM_KILLFOCUS` to the
host HWND, whose handler (`NativeHwndHost.cs:343-353`) immediately **detaches the queues
the attach path just set up**. Detaching `AttachThreadInput` resets the separated queues'
focus state — note `focus=0x0`: at that instant *no window on either thread has keyboard
focus*. Whether typing works afterwards depends on which message won the race — which is
precisely the reported symptom: "keyboard input sometimes works, sometimes doesn't."
The same attach→killfocus→detach→`focus=0x0` pattern recurs at 17:54:32 and 17:56:12,
followed by `setFocus=0x0` failures (cross-thread `SetFocus` failing because the
attachment is already gone).

**(b) Capture permanently corrupts guests (Windows 11 Notepad).**

```
07:54:47.128 DIAG[pre-capture]  hwnd=0x7904AA class=Notepad dpi=120 awareness=PerMonitorV2
07:54:47.379 DIAG[post-capture] hwnd=0x7904AA class=Notepad dpi=96  awareness=Unaware
07:54:47.990 Released 0x7904AA ...
07:54:48.806 DIAG[pre-capture]  hwnd=0x7904AA ... dpi=96 awareness=Unaware   <-- still broken AFTER release
```

`SetParent` across DPI-awareness contexts downgraded Notepad's window from
Per-Monitor-V2 @120 DPI to **Unaware @96 — and release did not restore it**, because a
window's DPI-awareness context cannot be reassigned after creation. The guest window is
permanently damaged (blurry, wrong-sized) until the app recreates it. No amount of
restore-styles-and-placement code in `WindowCaptureService.Release` can undo this.

**(c) Notepad self-destructs under capture.** In the 13:44 session, Notepad was captured,
the drift watchdog immediately had to snap it back (it repositioned itself in reaction to
the forwarded DPI change), and then:

```
13:44:55.148 LAYOUT[dpi-forward] ... result=failed          <-- WriteProcessMemory/WM_DPICHANGED forward failed
13:44:56.091 LAYOUT[drift] guest=...rect=384,544... host=192,272...   <-- guest fighting the clamp
13:44:56.801 WinEvent: captured window 0x501282 destroyed; removing its tab.
```

Modern Notepad is a WinUI/XAML-Islands *tabbed* application; reparenting + synthetic DPI
messages + geometry clamping caused it to tear down its HWND ~1.7 s after capture.
This is the user-reported "a simple Notepad cannot be opened in there." It is not a
TabDock bug in the narrow sense — it is the OS-level contract violation surfacing.
Every app family that recreates HWNDs at will (WinUI 3, UWP/ApplicationFrameHost, tabbed
apps, Chromium under GPU-process restart) is unhostable under HWND-identity capture.

**(d) Typing floods the UI thread.** Untitled Windows 11 Notepad documents mirror
document content into the window title, so *every keystroke* fires
`EVENT_OBJECT_NAMECHANGE`. Each one is posted to the UI thread, matched against all
groups, written **synchronously to disk** by the logger, and fanned out to
`RefreshTabTitles()` across all tabs (`App.xaml.cs:317-339`). The log records dozens of
`title changed` lines per second while typing. Browsers do the same on every page
navigation.

**(e) Frustration, quantified.** The logs record ~258 app restarts in six days
(each normal exit logs an `EMERGENCY RELEASE`), including four launch-exit cycles within
three minutes on 2026-07-17 (13:44–13:47). Two hard crashes are recorded
(`ReorderTabs` `ArgumentOutOfRangeException`; picker `Owner`-of-closed-window
`InvalidOperationException`) — both since patched, both of the whack-a-mole class.

### 2.3 The codebase already knows

`AGENTS.md` "Known limitations" concedes black/frozen GPU apps, elevated windows, mixed
DPI, and `taskkill` child destruction. The survival spike (`Spike/TabDock.Spike`) proved a
reparented child does not survive host force-kill. `RenderHealthService` exists solely to
detect guests that the architecture broke and eject them. A service whose job is
*detecting that the core mechanism failed* is the clearest possible sign the core
mechanism is the problem.

---

## 3. Root-cause analysis: why bugs keep reappearing

### RC1 — Cross-process `SetParent` is an unsupported composition model (Critical)

`WindowCaptureService.Capture` (`Services/WindowCaptureService.cs:80`) reparents a foreign
top-level window into `NativeHwndHost`'s child HWND, strips seven style bits, and rewrites
its ex-style. From that moment:

- The guest's and host's threads **share an input queue** (implicit `AttachThreadInput`
  semantics of cross-process parent/child). Any stall in either process stalls input in
  both → the global "lagginess", worst with heavyweight guests (browsers, editors).
- The guest still believes it is (or was) top-level: it processes `WM_DPICHANGED`,
  maximize/restore, its own move/size logic, monitor changes, etc. with wrong assumptions.
  Hence the drift watchdog, the zoom clamp, the reclamp retry timer, the move/size hooks —
  four separate control loops (`Views/ContainerWindow.xaml.cs:45-55,161,556-646`) fighting
  the guest's own logic, capped by a give-up counter that leaves the tab visibly wrong.
- DWM/compositor state (Chromium surfaces, WinUI islands) is invalidated in ways no
  outside process can repair → black/frozen tabs, `RenderHealthService`, the
  minimize/restore "compositor nudge" (`WindowCaptureService.cs:487-494`).
- DPI-awareness of the guest window is silently and *irreversibly* rewritten (§2.2b).

Every one of these has spawned its own subsystem. The subsystems interact (e.g. the drift
clamp re-triggers guest DPI reactions; the DPI forward triggers guest self-moves that trip
the drift clamp), which is why fixing one regresses another.

### RC2 — The input/focus machinery is a re-entrant state machine with five owners (Critical)

Attach/detach of `AttachThreadInput` is triggered from: tab switch
(`NativeHwndHost.SwitchActiveWindow`), container `WM_ACTIVATE`
(`ContainerWindow.xaml.cs:97-118`), host `WM_SETFOCUS` / `WM_KILLFOCUS`
(`NativeHwndHost.cs:330-353`), the drift watchdog's hung-guest check, and
container/app teardown. But `SetFocus` and `AttachThreadInput` *themselves synchronously
raise* `WM_SETFOCUS`/`WM_KILLFOCUS` on the same thread, so the handlers re-enter the state
machine mid-transition (§2.2a). There is no reliable invariant for "attached" ↔ "guest
focused"; the log proves states like *attached-but-unfocused* and *nobody-has-focus*.

Compounding it:

- `HasFocusWithinCore` returns `true` unconditionally while a tab is active
  (`NativeHwndHost.cs:70-82`). This lies to WPF's focus manager for the entire lifetime of
  an active tab, which degrades WPF's own keyboard handling elsewhere in the window (the
  rename TextBox, tab-strip interactions) — the other half of "my inputs won't come in."
- Synthetic `WM_ACTIVATE` is *sent* (blocking) to guests (`GuestActivationHelper.cs:66`,
  `WindowCaptureService.cs:441`) to trick them into thinking they're active. Apps that
  cross-check with `GetActiveWindow`/`GetFocus` see a contradiction; blocking `SendMessage`
  into a busy guest stalls the UI thread (the `IsHungAppWindow` guard only trips after ~5 s
  of hang).

### RC3 — DPI reconciliation cannot be won at this altitude (High)

Three generations of strategy coexist in the code: capture-time `WM_DPICHANGED` forwarding
via `WriteProcessMemory` into the guest's address space (`NativeMethods.cs:838-871` — it
*failed* for packaged Notepad in the log), a guest-to-host scale factor multiplied into
every layout (`DpiService.GetGuestToHostScaleFactor`, `WindowCaptureService.Layout:190`),
and host-side physical-pixel sizing (`NativeHwndHost.ArrangeOverride`). Each was added for
one app family and broke another (see §2.1 commit chain). Root cause: a reparented window
*keeps* its original awareness context (or gets force-downgraded), and Windows provides no
supported way to host mixed-awareness trees across processes without
`SetThreadDpiHostingBehavior` cooperation from *both* sides — which foreign apps will
never provide.

### RC4 — Hot paths run synchronous I/O and cross-process calls on the UI thread (High)

- `LoggingService.Log` opens-appends-closes the log file (plus a `FileInfo` stat) under a
  lock **per line** (`Services/LoggingService.cs:25-44`), and is called from focus
  handlers, layout, drift ticks, and WinEvent handlers — including per-keystroke
  NAMECHANGE storms (§2.2d). Disk stalls (AV scans, sync clients) translate directly into
  input lag, and via the shared input queue, into *guest* input lag.
- Six **system-wide** WinEvent hooks (`Services/WinEventMonitor.cs:69-78`) deliver every
  destroy/hide/name-change/minimize/move-size event of *every window in the session* to
  TabDock's UI thread, each running the groups×members filter and (post-filter) more
  handlers + logging. Browsers alone generate constant NAMECHANGE traffic.
- `SetWindowPos`/`SendMessage`/`PrintWindow` against guest windows are synchronous with
  the guest's message pump; called from the UI thread (layout, clamps, activation,
  release), they stall TabDock for as long as the guest is busy.

### RC5 — Reliability mechanisms that silently do nothing (Medium)

- **The guest subclass never installs.** comctl32 `SetWindowSubclass` cannot subclass a
  window owned by another process, so the SC_MAXIMIZE/SC_RESTORE clamp
  (`WindowCaptureService.cs:148-154, 272-282`) fails on every real capture — the log
  confirms `DIAG[subclass-top] ... result=failed error=No error` on each one. The intended
  protection does not exist; only the WinEvent fallback does.
- **Wrong P/Invoke signature:** `RemoveWindowSubclass` (`NativeMethods.cs:246`) is declared
  with 2 parameters; the real API takes 3 (`hWnd, pfnSubclass, uIdSubclass`). Harmless
  today only because the subclass never installs.
- **Render health is a one-shot**, 800 ms after capture (`RenderHealthService.cs:21`,
  `ContainerWindow.CheckRenderHealthAsync`), despite being described as a recovery loop
  (CLAUDE.md). A guest that goes black later (GPU process restart, maximize black-out —
  a known limitation) is never re-checked.
- **Emergency release cannot cover the worst case** (host force-kill destroys child HWNDs
  — proven by the spike). In the current model this is unfixable; the recommended model
  (§6) removes the exposure entirely.

### RC6 — Assorted defects worth knowing about (Low, illustrative)

- Capture picker doesn't filter DWM-cloaked windows (`CapturePickerViewModel.Refresh`),
  so ghost UWP/suspended windows are listed; icon extraction does disk I/O per window on
  the UI thread while the dialog opens.
- `Release` restores placement but can only *guess* activation back to the released
  window; the Chromium "compositor nudge" (minimize+restore) is a visible flicker hack.
- `GroupViewModel.AccentBrush` allocates a converter per binding read; trivial waste.
- `WinEventMonitor` correctness now depends on `IsCapturedWindow` being safe from the
  hook thread — true today only because the hook is registered on the UI thread; nothing
  enforces it.
- The three overlapping clamp mechanisms (drift timer, MOVESIZEEND reclamp, reclamp-retry
  timer) each have separate guest-state guards; any future edit must keep all three
  consistent — high regression surface (this is where the H2 oscillation lived).

**What is *not* wrong:** the MVVM structure, the persistence design (layout intent only),
the no-nesting invariants, the guarded-spawn discipline, the logging/diagnostic culture,
and the e2e/validation harnesses are all genuinely good. This is a well-built app on an
unbuildable foundation.

---

## 4. Symptom → root-cause map (user's complaints, explained)

| Reported symptom | Mechanism | Root cause |
|---|---|---|
| "Laggy, inputs won't come in" | Shared input queue with guest + UI-thread sync I/O + WinEvent firehose + blocking cross-process calls | RC1, RC2, RC4 |
| "Keyboard sometimes works, sometimes doesn't" | Re-entrant attach/SetFocus/detach race ending in `focus=0x0`; `HasFocusWithinCore` lie degrading WPF focus | RC2 |
| "Notepad can't be opened in there" | WinUI Notepad's HWND destroyed ~1.7 s after reparent; DPI awareness permanently downgraded | RC1, RC3 |
| "Bugs keep reappearing after being fixed" | Fixes are compensating patches against OS behavior; each new guest/DPI/update regenerates the symptom elsewhere | RC1–RC3 (systemic) |
| Black/frozen tabs (documented) | Compositor invalidation on reparent; one-shot health check misses late failures | RC1, RC5 |

---

## 5. The tech-stack question, answered directly

**Should TabDock be rewritten in Rust?** No — not to fix these bugs.

- Every critical bug above lives in Win32 API semantics (`SetParent`,
  `AttachThreadInput`, `WM_DPICHANGED`, WinEvents, DWM). A Rust rewrite calls the same
  APIs and inherits the same behavior, bug-for-bug. Rust's strengths (memory safety, data-race
  freedom, no GC) address failure classes this app does not have: there are zero
  memory-corruption bugs, zero GC-pressure symptoms, and the "threading bugs" are Win32
  input-queue semantics, not data races.
- WPF is not the bottleneck. The XAML layer (tab strip, picker, chrome) is the most
  reliable part of the app, and `HwndHost` interop is only painful *because of* the
  reparenting model. .NET 8 + WPF also gives the self-contained single-file distribution
  the project already relies on.
- A rewrite costs weeks and yields parity at best. Re-architecting the windowing model
  inside the existing stack (§6) removes the bug classes outright and *deletes* more code
  than it adds.
- If Rust is attractive for its own sake, the sane insertion point later is a tiny
  out-of-process helper (e.g. a hidden-window rescue journal, §6.5) — not the UI app.
  The same is true of C++/WinUI/Electron/anything: **the language was never the variable;
  the windowing model is.**

---

## 6. Recommended architecture: coordinate windows, don't adopt them

Replace reparenting with a **synchronized-overlay ("shepherd") model**, the family of
techniques used by commercial window-tabbing/-tiling utilities that stay stable across
Windows releases (tab-band overlays like TidyTabs; position-orchestration like FancyZones
/ PowerToys Workspaces — none of which reparent foreign windows):

### 6.1 Core model

- A captured app **remains an unmodified top-level window**. No `SetParent`, no style
  stripping, no DPI messages, no subclassing, no input attachment. Ever.
- The TabDock container stays a WPF window: tab strip + (now empty) content area.
- **Active tab:** the guest window is positioned with `SetWindowPos` to exactly cover the
  container's content area, kept immediately above the container in z-order.
- **Inactive tabs:** `ShowWindow(SW_HIDE)` (removes them from screen, taskbar, and
  alt-tab — same UX as today's hidden tabs).
- **Release / pop-out:** re-show at recorded bounds. Since nothing about the window was
  mutated, "restore" is `SetWindowPlacement` + done — the entire class of
  restore-styles/exstyle/owner/frame bugs disappears.

### 6.2 Synchronization loop (the only moving part)

- Container move/size/minimize/maximize → reposition the active guest (one deferred
  `SetWindowPos`; coalesce on `WM_EXITSIZEMOVE` + throttled during drag).
- Guest self-move/resize (existing `EVENT_SYSTEM_MOVESIZEEND` + a per-guest
  `EVENT_OBJECT_LOCATIONCHANGE` hook scoped to the guest's thread — cheap, not
  system-wide) → snap back or, for user drags beyond a threshold, treat as drag-out
  release. This replaces today's three clamp mechanisms with one.
- Foreground tracking (existing `EVENT_SYSTEM_FOREGROUND` hook): guest activated → raise
  container just beneath it; container activated → forward activation to the active guest
  with `SetForegroundWindow` (legal here: TabDock is the foreground process at that
  moment). **No `AttachThreadInput` anywhere.**
- Destroy/title/minimize hooks: keep the existing `WinEventMonitor` design, but register
  hooks **per guest thread/process** (`SetWinEventHook(idProcess, idThread)`) instead of
  system-wide — the UI-thread firehose (RC4) disappears.

### 6.3 What this buys, symptom by symptom

| Today | Shepherd model |
|---|---|
| Shared input queues → lag, dead keyboard | Input goes directly to the guest; TabDock never touches focus internals |
| DPI downgrade, scale-factor math, forwarding | Guest stays top-level; OS delivers real `WM_DPICHANGED`; delete `DpiService` reconciliation |
| Black Chrome/Electron/WinUI tabs, render health service | Guest renders itself normally; delete `RenderHealthService` |
| Notepad/WinUI HWND destruction | Nothing invasive happens to the window; tabbed/HWND-recreating apps just work (track recreation via per-process hooks) |
| taskkill on TabDock destroys guests | Worst case: guests remain alive but hidden (recoverable, §6.5) |
| Style/owner/placement restore bugs on release | Nothing was changed; nothing to restore |

### 6.4 Honest trade-offs (decide with eyes open)

1. **Z-order seams.** Another window can theoretically slot between container and guest
   for a frame during rapid alt-tabbing; mitigated by foreground-event handling. This is
   the main polish cost of the model — and it is cosmetic, unlike today's failures.
2. **Guest title bars remain visible** inside the "tab" by default. Options, in order of
   invasiveness: live with it (v1); size the guest so its caption sits under the tab strip
   (crop trick); or *reversibly* strip `WS_CAPTION` while docked — style-only, no
   reparent; test per-app and be ready to fall back (some apps redraw custom frames).
3. **Minimize semantics:** hiding inactive tabs removes taskbar entries (same as today).
   Minimizing the container should hide the active guest too.
4. **A hidden guest whose tab is lost** (TabDock crash) needs rescue (§6.5). Note this is
   strictly better than today, where the guest HWND is *destroyed*.

### 6.5 Crash safety in the new model

Journal every hide (`HWND`, PID, exe, original placement) to
`%APPDATA%\TabDock\hidden-windows.json` before hiding; clear entries on re-show. On
startup (and via a `--rescue` flag), re-show any journaled windows whose process is still
alive. Emergency-release paths shrink to "re-show everything in the journal" — no style
surgery, no ordering constraints.

### 6.6 What survives unchanged

`GroupManager`, `PersistenceService`, all ViewModels, the picker, the container chrome/tab
strip UI, `HotkeyService`, `IconService`, logging (with the async fix below), the guarded
spawn pattern, and both test harnesses (the e2e harness's capture/release assertions get
*simpler*: parent/style must be *unchanged*).

---

## 7. Migration plan

### Phase 0 — Stop the bleeding (small, in-place, current model; ~days)

Worth doing even if Phase 1 starts immediately:

1. Remove the `WM_SETFOCUS`/`WM_KILLFOCUS` attach/detach handlers in
   `NativeHwndHost.HostWndProc` — they are the re-entrancy engine of §2.2a. Attach only on
   tab-switch and container `WM_ACTIVATE`; guard against re-entrant detach with an
   in-transition flag.
2. Make `LoggingService` a bounded background queue (single writer thread, drop-oldest on
   overflow); never touch disk on the UI thread.
3. Debounce NAMECHANGE handling (e.g. 250 ms per HWND) before logging/refreshing titles.
4. Delete the dead `SetWindowSubclass` path (it never installs; it logs a failure per
   capture and keeps a wrong P/Invoke signature around).
5. Filter DWM-cloaked windows from the picker (`DwmGetWindowAttribute(DWMWA_CLOAKED)`).
6. Re-run render-health on `EVENT_OBJECT_HIDE`-adjacent signals or a slow timer rather
   than once-at-capture, or accept and document the one-shot.

### Phase 1 — Shepherd-mode prototype (~1–2 weeks)

- New `WindowShepherdService` implementing §6.1–6.2 alongside the existing capture
  service, behind a "dock mode" toggle (per group). `NativeHwndHost` shrinks to a
  placeholder (or is replaced by a plain Border measured for the sync rect).
- Acceptance tests: Notepad (WinUI), Chrome, Windows Terminal, an Electron app, Task
  Manager-kill of TabDock, mixed-DPI monitor drag — i.e., today's known-broken matrix.

### Phase 2 — Cutover and deletion

- Default new groups to shepherd mode; keep legacy capture behind a flag for one release.
- Then delete: reparenting in `WindowCaptureService`, all `AttachThreadInput`/focus
  machinery in `NativeHwndHost`, `GuestActivationHelper`, DPI reconciliation in
  `DpiService`, `RenderHealthService`, the drift/reclamp timers, `TrySendDpiChanged`
  (`WriteProcessMemory`) and the subclass P/Invokes. Estimated net: **−1,500 lines** and
  the removal of every Critical/High root cause above.

### Phase 3 — Polish (optional)

Reversible caption-strip while docked; taskbar/alt-tab refinement; snap animations;
"re-populate group" wizard using the persisted exe paths (infrastructure already exists).

---

## 8. Finding index (ranked)

| # | Severity | Finding | Location |
|---|---|---|---|
| F1 | Critical | Cross-process `SetParent` reparenting as core model: shared input queues, compositor breakage, irreversible guest DPI downgrade, HWND-recreating apps unhostable | `WindowCaptureService.Capture` (`Services/WindowCaptureService.cs:80-114`) |
| F2 | Critical | Re-entrant attach/focus/detach state machine; `SetFocus` triggers host `WM_KILLFOCUS` which detaches mid-attach; detach resets queue focus to NULL (log-confirmed) | `Infrastructure/NativeHwndHost.cs:130-137, 201, 330-353`; `Views/ContainerWindow.xaml.cs:97-118` |
| F3 | High | Persistent `AttachThreadInput` design couples TabDock's input to arbitrary guest processes (mutual input starvation) | `Infrastructure/NativeHwndHost.cs:33-37, 88-143` |
| F4 | High | DPI model unwinnable: three coexisting strategies; `WriteProcessMemory`-based `WM_DPICHANGED` forward fails for packaged apps; guest awareness permanently downgraded across capture (log-confirmed) | `NativeMethods.cs:838-871`, `WindowCaptureService.cs:137, 190-192, 232-263` |
| F5 | High | Synchronous per-line file logging on UI-thread hot paths (focus, layout, WinEvents, per-keystroke NAMECHANGE storms) | `Services/LoggingService.cs:25-44`; `App.xaml.cs:317-339` |
| F6 | High | Six system-wide WinEvent hooks funnel session-wide event traffic through the UI thread | `Services/WinEventMonitor.cs:69-78` |
| F7 | Medium | `HasFocusWithinCore` unconditionally `true` while a tab is active — lies to WPF focus manager, degrades WPF-side input (rename box etc.) | `Infrastructure/NativeHwndHost.cs:70-82` |
| F8 | Medium | Guest `SetWindowSubclass` can never install cross-process (fails every capture); `RemoveWindowSubclass` P/Invoke signature wrong (2 args vs 3) | `WindowCaptureService.cs:148-154`; `NativeMethods.cs:242-249` |
| F9 | Medium | Synthetic blocking `WM_ACTIVATE` sends to guests (false activation state; UI-thread stall on busy guest) | `GuestActivationHelper.cs:50-68`; `WindowCaptureService.cs:441` |
| F10 | Medium | Render health is a one-shot 800 ms check, not the documented recovery loop; late black-outs undetected | `Services/RenderHealthService.cs:19-21`; `ContainerWindow.CheckRenderHealthAsync` |
| F11 | Medium | Three overlapping geometry-clamp mechanisms (1 Hz drift timer, MOVESIZEEND reclamp, retry timer) with duplicated guards; give-up counter leaves visibly wrong state; `SetWindowPos` to busy guests stalls UI thread | `Views/ContainerWindow.xaml.cs:45-55, 514-646` |
| F12 | Low | Picker lists DWM-cloaked ghost windows; per-window icon disk I/O on UI thread during open | `ViewModels/CapturePickerViewModel.cs:72-101` |
| F13 | Low | Chromium "compositor nudge" (minimize+restore) on release — visible flicker hack papering over F1 | `WindowCaptureService.cs:487-494` |
| F14 | Low | `AccentBrush` allocates a converter per read; `WinEventMonitor` thread-affinity of `IsCapturedWindow` unenforced | `ViewModels/GroupViewModel.cs:32`; `Services/WinEventMonitor.cs:118-147` |

---

## 9. Bottom line

TabDock's craftsmanship is good; its foundation is fighting the operating system.
Cross-process reparenting guarantees the exact symptom set observed — recurring bugs, input
loss, lag, and modern apps (Notepad included) that cannot be hosted at all — and no
patch, language, or framework changes that. Move to the coordinate-don't-adopt model in
§6: it keeps ~80 % of the codebase, deletes the five most dangerous subsystems, and turns
the remaining hard problems from "unsupported by Windows" into "ordinary engineering."
