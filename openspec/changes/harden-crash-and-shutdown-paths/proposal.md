## Why

Several crash/exit paths and modal-loop re-entrancy seams in `App.xaml.cs` and `ContainerWindow` break the project's single-UI-thread discipline or leave incoherent state:

- `CurrentDomain_UnhandledException` (`App.xaml.cs:184-198`) runs on the faulting (arbitrary) thread and calls `SaveState()` (enumerates live `Groups`/`Members` — `PersistenceService.cs:36,51`), `EmergencyReleaseAll()`, and `FlushJournal()`, none of which is synchronized against concurrent UI-thread mutation. A torn enumeration silently skips the crash-time save exactly when it matters; a concurrent journal `RemoveAll`/`Add` can corrupt the recovery journal. It also calls `DispatcherTimer.Stop()` off-thread (`GroupManager.cs:92`).
- `Application_SessionEnding` (`:200-215`) releases every guest via `EmergencyReleaseAll` but never removes members from `Group.Members` or clears `_capturedIndex`. If the logoff is cancelled, TabDock keeps running with hooks installed and container move-sync repositioning now-unmanaged standalone windows.
- The capture-picker path (`ShowCapturePickerCore`, `:592-614`) calls `CreateGroup()` + `OpenContainer(group)` with no try/catch, unlike `OnNewGroupRequested` (`:509-524`) which rolls back — a container-less group is saved and re-opened on every subsequent launch (crash loop), and the exception escapes the modal loop, terminating the app over a routine failure.
- The startup-failure catch (`:128-141`) doesn't set `ContainerWindow.IsAppShuttingDown` (every other exit path does), so the close-confirm Yes/No/Cancel modal can appear during a fatal shutdown.
- `ContainerWindow_Closing`'s `MessageBox` (`ContainerWindow.xaml.cs:256-261`) pumps queued WinEvent callbacks in a nested dispatcher loop: a guest destroying itself mid-prompt can re-enter `Close()` on a window already in `Closing`, and the global hotkey can stack a capture picker on top of the close prompt.
- `OpenContainer` (`App.xaml.cs:648-656`): if `new ContainerWindow(...)` or `Show()` throws after the VM subscribed `_group.PropertyChanged` in its constructor, the VM leaks (held by the long-lived `Group`); `Detach()` only runs from `ContainerWindow_Closed`.
- Deferred `WinEventMonitor.Stop` (`:332-336`) can fire after `Application_Exit` disposed `_events`.

## What Changes

- **Crash-path threading** — in `CurrentDomain_UnhandledException`: when `e.IsTerminating`, do only thread-safe logging; otherwise marshal `SaveState`/`EmergencyReleaseAll`/`FlushJournal` through `Dispatcher.Invoke` with a short timeout, falling back to log-only on timeout.
- **SessionEnding coherence** — after releasing, clear each group's `Members` (index maintenance follows via `CollectionChanged`) and stop the monitor, so a cancelled logoff leaves coherent state.
- **Picker rollback** — wrap each `OpenContainer(group)` in `ShowCapturePickerCore` in the same try/catch + `_groups.RemoveGroup(group)` rollback as `OnNewGroupRequested`, surfacing a `MessageBox` instead of throwing.
- **Startup-failure flag** — set `ContainerWindow.IsAppShuttingDown = true` as the first statement of the `Application_Startup` catch.
- **Modal re-entrancy guard** — add a `_closePromptOpen` guard (same pattern as `_pickerOpen`): defer `EmptiedByPopOut`-driven `Close()` and picker requests until the prompt returns; re-check `_viewModel.Tabs.Count` after the `MessageBox` returns.
- **VM leak** — try/catch around container construction/`Show()` in `OpenContainer`; call `vm.Detach()` on failure.
- **Deferred stop guard** — guard the deferred `_events.Stop()` with a disposed/shutdown flag.
- **Adjacent one-liner** — `HotkeyService.Register`'s `Handle == IntPtr.Zero` branch is dead (`HwndSource` ctor throws instead); wrap construction in try/catch and log.

## Capabilities

### New Capabilities
- `crash-shutdown-coherence`: Thread-safe crash handlers, coherent session-ending release, prompt-free fatal shutdown, container-open rollback, and re-entrancy-safe close confirmation.

### Modified Capabilities
(none — crash-path and modal-loop behavior hardening; no spec-visible behavior change beyond "shutdown doesn't prompt and crash saves actually happen".)

## Impact

- **Code**: `App.xaml.cs`, `Views/ContainerWindow.xaml.cs`, `Services/GroupManager.cs` (clear-on-session-ending), `Services/HotkeyService.cs`.
- **Risk**: low/medium — the changes touch every exit path; each exit path must be re-verified after implementation.
