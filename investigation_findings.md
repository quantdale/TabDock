# TabDock — Investigation Findings (read-only audit)

Comprehensive read-only review of the TabDock application, ordered by severity.
Produced by the 2026-07-11 investigation session. Findings are documented only —
none of these were fixed in this session unless explicitly cross-referenced to the
session's fix scope (Tasks 2–5), in which case the entry says so.

Line numbers refer to the source as of this session (after the Task 2/4/5 fixes and
the Task 3 maximize fix were applied).

**Runtime-confirmed during validation:** H1 (render-health auto-release never runs on
the real capture path) and M4 (black-frame detector misses opaque black) together mean
the documented "release a black tab back to standalone" recovery does not fire — this is
the same area as the still-open GPU/Electron black-content-on-maximize limitation that
this session's Task 3 fix (work-area clamp) only partially mitigates (see the session
report). L5 (stray-semicolon glyph) was fixed incidentally during Task 3 cleanup. A new
Low finding (L11, empty container after popping out the last tab) was observed during
repeated-cycle validation.

---

## HIGH

### H1. Render-health auto-release is never invoked for captured windows (documented recovery feature is dead)
- **Where:** `Views/ContainerWindow.xaml.cs` (`CaptureWindow` vs `AddCapturedWindow`); `Services/WindowCaptureService.cs:123-128`
- **Description:** CLAUDE.md describes render health as "a recovery loop … triggers automatic release back to standalone." The only method that actually releases an unhealthy window, `ContainerWindow.AddCapturedWindow` → `CheckRenderHealthAsync`, has **no callers**. The real capture path (`ContainerWindow.CaptureWindow` → `_viewModel.AddCapturedWindow`) never runs a health check. `WindowCaptureService.Capture` schedules its own check that only *logs* and sets `cw.RenderHealth` — a flag nothing reads.
- **Why it matters:** Black/frozen GPU/Electron/DirectX tabs — the exact failure the feature exists for — are never auto-released. The window stays as a dead-looking black tab.
- **Impact:** Core documented resilience behavior is non-functional.
- **Resolution options:** Route the normal capture path through `ContainerWindow.AddCapturedWindow` (or call `CheckRenderHealthAsync` from `CaptureWindow`), and consume `cw.RenderHealth`.
- **Severity:** High

### H2. Drag-reorder oscillation: reorder fires on every MouseMove against a mutating layout
- **Where:** `Views/ContainerWindow.xaml.cs` (`TabsListBox_MouseMove`, `GetDropIndex`); `ViewModels/GroupViewModel.cs` (`ReorderTabs`)
- **Description:** Every `MouseMove` during a drag computes `GetDropIndex(pos)` from live container geometry with a single midpoint threshold and no hysteresis, and immediately reorders. After a reorder the item positions swap under a stationary cursor, so the next `MouseMove` computes the opposite index and reorders back. The runtime log records dozens of 1↔2 flips per second during a real user drag (13:21 session). The prior fatal crash (`Collection.Insert` out of range from this same path, logged 07:31 with a full stack trace) is now clamped, but the jitter remains, and `ContainerFromIndex`/`ActualWidth` may reflect pre-reorder layout for a frame, returning null or wrong positions.
- **Why it matters / impact:** Constant reordering, re-selection, `ObservableCollection` churn, `Group.ActiveIndex` mutation and log spam during a single drag; visually unstable tab strip; latent index-staleness bugs.
- **Resolution options:** Commit the reorder only on `MouseUp`; or add hysteresis (require crossing the full neighbor bounds, dead-band around the current slot); or use an insertion-adorner preview and reorder once. Cache container bounds at drag start.
- **Severity:** High

### H3. Global hotkey after MainWindow is closed throws (Owner set to a closed window)
- **Where:** `App.xaml.cs` (`ShowCapturePicker`); `App.xaml` (`ShutdownMode="OnLastWindowClose"`)
- **Description:** `_mainWindow` is never nulled when the launcher closes. With a container still open the app stays alive; pressing Ctrl+Alt+G then calls `ShowCapturePicker`, which sets `picker.Owner = _mainWindow` on a **closed** Window — `InvalidOperationException`.
- **Impact:** A normal user action (close launcher, keep a group open, hit the hotkey) crashes the dispatcher → emergency release + `Shutdown(1)`.
- **Resolution options:** Null `_mainWindow` in its `Closed` handler and guard/recreate before use; or only set `Owner` when `_mainWindow.IsLoaded`; or keep the launcher alive/hidden.
- **Severity:** High

---

## MEDIUM

### M1. `SetParent` NULL-return treated as failure — parentless top-level windows misclassified
- **Where:** `Services/WindowCaptureService.cs:72-81`
- **Description:** `SetParent` returns the *previous parent*, which is NULL for a genuinely parentless top-level window even on **success**; failure is distinguished only by `GetLastError`. The code treats any `IntPtr.Zero` return as failure and aborts — *after* the reparent took effect but *before* style fixup and `CapturedWindow` creation, leaving the guest reparented into the host untracked/unrestyled on that branch.
- **Impact:** Either valid captures rejected, or an orphaned guest stuck inside the host with no tracking entry (severity rises to High if common windows actually return NULL on success — verify at runtime against Notepad).
- **Resolution options:** `SetLastError(0)` before the call; treat `prev==NULL && GetLastError()!=0` as the only failure; on any post-`SetParent` abort, reparent the guest back.
- **Severity:** Medium

### M2. `EVENT_SYSTEM_FOREGROUND` hook installed but `WindowForegroundChanged` has no subscriber
- **Where:** `Services/WinEventMonitor.cs`; `App.xaml.cs` (`WireWinEvents`)
- **Description:** Every system-wide foreground change is filtered through `IsCapturedWindow` and posted to the UI thread to invoke an empty event. Presumably intended for external-foreground → active-tab sync; never wired.
- **Impact:** Wasted work on a high-frequency global event; dead feature.
- **Resolution options:** Wire a handler that syncs the active tab, or stop installing the hook.
- **Severity:** Medium

### M3. Global WinEvent hooks fire for every window system-wide; per-event O(N) scan on the UI thread
- **Where:** `Services/WinEventMonitor.cs`; `Services/GroupManager.cs` (`IsCapturedWindow`)
- **Description:** Hooks use `idProcess=0, idThread=0`. `EVENT_OBJECT_HIDE`/`DESTROY` are extremely chatty system-wide. The `idObject!=0` early-out is a good mitigation, but `IsCapturedWindow` is a linear scan of all groups/members run for every top-level hide/destroy on the machine, on the UI thread (out-of-context hooks deliver on the installing thread).
- **Impact:** UI-thread overhead proportional to *global* window churn.
- **Resolution options:** Back `IsCapturedWindow` with a `HashSet<IntPtr>` maintained on add/remove; consider per-thread hook scoping.
- **Severity:** Medium

### M4. Black-frame detection can't detect opaque RGB-black
- **Where:** `Services/RenderHealthService.cs` (`IsUniformZero`)
- **Description:** A frame is flagged unhealthy only if every 32-bit pixel is exactly `0x00000000`. `PrintWindow(PW_RENDERFULLCONTENT)` typically produces opaque output (`0xFF000000` for black) → black tabs read as "healthy."
- **Impact:** False "healthy" verdicts for exactly the failure the service exists to catch (compounds H1).
- **Resolution options:** Mask off alpha and compare RGB against near-black; or detect uniform-color frames of any color with tolerance.
- **Severity:** Medium

### M5. Persistence save timing leaves most state changes unsaved except on clean exit
- **Where:** `App.xaml.cs`; `Views/ContainerWindow.xaml.cs`; `Services/PersistenceService.cs`
- **Description:** `SaveState` runs only on clean `Application_Exit`, rename commit, and accent-color change (the latter two added this session). Group creation, capture, release, reorder, and active-tab changes are not persisted until then; crash handlers run `EmergencyReleaseAll` but not `SaveState`.
- **Impact:** Group layout intent lost on crash or hard kill.
- **Resolution options:** Debounced save on group create/capture/release/reorder; add `SaveState` to crash handlers.
- **Severity:** Medium

### M6. `ContainerWindow_Closing` shows a modal MessageBox per container during app shutdown
- **Where:** `Views/ContainerWindow.xaml.cs` (`ContainerWindow_Closing`); `App.xaml.cs` (Exit path)
- **Description:** The `Tabs.Count==0` guard covers the programmatic empty-group close, but "Exit" with populated containers open fires the Yes/No/Cancel prompt once per container, and Cancel during `Shutdown` is ambiguous.
- **Impact:** Confusing multi-prompt exit; possible half-closed states.
- **Resolution options:** `_isShuttingDown` flag set by the Exit path → skip prompt, straight release.
- **Severity:** Medium

### M7. GDI/icon handle leak on exception in `IconService.GetFileIcon`
- **Where:** `Services/IconService.cs:33-64`
- **Description:** If `CreateBitmapSourceFromHIcon` throws, the `catch { return null; }` skips `DestroyIcon` for both extracted handles.
- **Impact:** Slow GDI-object growth under repeated capture/picker refresh.
- **Resolution options:** `try/finally` with `DestroyIcon` for both handles.
- **Severity:** Medium

### M8. Layout uses raw device pixels; `DpiService` is entirely unused (dead mitigation machinery)
- **Where:** `Services/WindowCaptureService.cs` (`Layout`); `Services/DpiService.cs`; unused `_dpi` fields in `WindowCaptureService` and `RenderHealthService`
- **Description:** `Layout` sizes the guest to the host's `GetClientRect` (device px) with no DPI reconciliation; `DpiService`'s entire public surface is unreferenced.
- **Impact:** Sizing artifacts for mixed-DPI-awareness guests / cross-monitor moves; dead code implying handling that doesn't exist.
- **Resolution options:** Apply `DpiService` in `Layout`/release, or delete it and document the limitation.
- **Severity:** Medium

---

## LOW

### L1. Dead methods
`GroupManager.CaptureIntoGroup` (no callers; would desync `Group.Members` from `GroupViewModel.Tabs` if ever used — latent trap) and `WindowCaptureService.ReleaseAndShow` (trivial unused wrapper). **Resolution:** remove or unify the capture path. **Severity:** Low

### L2. Dead commands/events in `GroupViewModel`; wrong-target placeholder
`StartRenameCommand`, `FinishRenameCommand`, `PickColorCommand`, `CloseGroupCommand` unbound in XAML; `CloseRequested` has no subscriber (so `CloseGroup()` firing it does nothing); `PickColorCommand` wrongly invokes `AddWindowsRequested` (self-described placeholder) — would open the capture picker if ever wired. **Severity:** Low

### L3. `ReleaseTab` and `OnPopOutRequested` are duplicated bodies
`OnPopOutRequested` is `ReleaseTab(tab)` with the body copy-pasted. **Resolution:** delegate one to the other. **Severity:** Low

### L4. Unused private fields / never-read property (analyzer warnings)
`GroupViewModel._capture`, `WindowCaptureService._dpi`, `RenderHealthService._dpi`; `CapturedWindow.RenderHealth` written but never read. **Severity:** Low

### L5. Maximize button glyph has a stray semicolon — FIXED THIS SESSION
The StateChanged handler used a variable-width `\x` hex escape that consumed the trailing `;` as a hex digit, leaving a stray glyph after each maximize/restore. **Fixed incidentally** during Task 3 cleanup by switching to fixed-width `\uXXXX` escapes. **Severity:** Low (resolved)

### L11. Empty container remains open after popping out the last tab
**Where:** `ViewModels/GroupViewModel.cs` (`OnPopOutRequested`); contrast `App.xaml.cs` `RemoveDeadMember`.
**Description:** The destroy/hide handlers close the now-empty container and remove the group, but popping out the **last** tab via the context menu or drag-out removes the tab and sets `ActiveTab = null`, leaving an empty container open. Observed during repeated-cycle validation: five pop-out cycles left multiple empty "Group" containers on screen.
**Why it matters:** Empty container windows accumulate (clutter/confusion). Not a correctness bug (no orphaned guests), and arguably intended (pop-out is not "close the group").
**Resolution options:** Either auto-close the container when the last tab is popped out (matching the destroy/hide path) or keep it open by design and document the difference. **Severity:** Low

### L6. Diagnostic instrumentation in production hot paths
This session's Task 3 instrumentation (`STATE[...]` snapshots + per-state-change health task; `LAYOUT[...]` per-layout lines; `HOST WM_SIZE` per-resize-tick lines) is verbose. The per-`WM_SIZE` and per-layout lines especially churn the 1 MB rotating log during interactive resizing. **Resolution:** after the maximize diagnosis is settled, downgrade to the low-volume subset (keep `STATE` snapshots and `LAYOUT[capture]`). **Severity:** Low

### L7. `Group.PropertyChanged` subscription in `GroupViewModel` never unsubscribed
Currently collectible together in all flows, but any future path keeping a `Group` alive after its container closes leaks the VM via the model's event. **Resolution:** unsubscribe in `CloseGroup`/`Dispose`. **Severity:** Low

### L8. Inconsistent `Groups` snapshotting in WinEvent handlers
`WindowMinimized`/`WindowNameChanged` iterate `_groups.Groups` directly; `WindowDestroyed`/`WindowHidden` use `.ToList()`. Safe today (the former don't mutate), but the asymmetry is a trap for future edits. **Resolution:** snapshot consistently. **Severity:** Low

### L9. Double emergency-release on dispatcher crash
`DispatcherUnhandledException` → `EmergencyReleaseAll` → `Shutdown(1)` → `Application_Exit` → `EmergencyReleaseAll` again. Largely idempotent (`IsWindow` guards) but re-runs `SetForegroundWindow` on already-released windows. **Severity:** Low

### L10. New `ColorToBrushConverter` allocated on every `AccentBrush` get
`GroupViewModel.AccentBrush` news up a converter per access. **Severity:** Low

---

## INFO

### I1. Docs claim block namespaces; entire codebase is file-scoped
`AGENTS.md`/`CLAUDE.md` state classic block namespaces are the convention (and give a file-scoped example while saying so); every `.cs` file uses file-scoped namespaces. Update the docs. **Severity:** Info

### I2. Restored `Group.ActiveIndex` is clamped to -1 on load
The setter clamps against empty `Members` during load, discarding the persisted active-tab intent. **Severity:** Info

### I3. Capture picker may list cloaked UWP / owned windows
No `DWMWA_CLOAKED` filter (constant exists, unused) nor `GW_OWNER` check → ApplicationFrameHost ghosts and owned popups can appear; capturing them will likely misbehave. **Severity:** Info

### I4. WinEventMonitor's UI-thread delivery assumption is correct today but unguarded
Out-of-context hooks deliver on the installing thread; `Start()` runs on the UI thread, so the synchronous `IsCapturedWindow` read is race-free — entirely dependent on the call site. The hide-teardown added this session additionally relies on Post-after-completion ordering (documented in code comments). **Resolution:** assert dispatcher-thread in `Start()`. **Severity:** Info

---

## Inferable compiler/analyzer warnings summary
- Unused fields: `GroupViewModel._capture`, `WindowCaptureService._dpi`, `RenderHealthService._dpi`.
- `CapturedWindow.RenderHealth` written, never read.
- `DpiService` public surface entirely unreferenced.
- Dead: `GroupManager.CaptureIntoGroup`, `WindowCaptureService.ReleaseAndShow`, `GroupViewModel.CloseRequested`, four unbound commands.
- Unused usings: `MainViewModel.cs` (`System.Linq`, `System.Collections.Generic`), `WinEventMonitor.cs` (`System.Windows.Threading`), `GroupManager.cs` (`System.Windows`).

