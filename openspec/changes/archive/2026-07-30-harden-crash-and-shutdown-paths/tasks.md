# Tasks

## 1. Crash-path thread affinity

- [x] 1.1 In `CurrentDomain_UnhandledException`, when `e.IsTerminating`, restrict the handler to thread-safe logging only
- [x] 1.2 When not terminating, marshal `SaveState`/`EmergencyReleaseAll`/`FlushJournal` through `Dispatcher.Invoke` with a short timeout; fall back to log-only on timeout

## 2. Session-ending coherence

- [x] 2.1 After `EmergencyReleaseAll` in `Application_SessionEnding`, clear each group's `Members` and let the `CollectionChanged`-driven index follow
- [x] 2.2 Stop the WinEvent monitor so a cancelled logoff leaves no live hooks over unmanaged windows

## 3. Capture-picker rollback

- [x] 3.1 Wrap `OpenContainer(group)` calls in `ShowCapturePickerCore` in try/catch with `_groups.RemoveGroup(group)` rollback and a `MessageBox` (mirroring `OnNewGroupRequested`)

## 4. Shutdown flags and guards

- [x] 4.1 Set `ContainerWindow.IsAppShuttingDown = true` first in the `Application_Startup` catch block
- [x] 4.2 Guard the deferred `_events.Stop()` in `SyncWinEventMonitor` against firing after `Application_Exit` disposed the monitor (explicit disposed/shutdown flag; today this is a no-op only incidentally, via `Stop`'s idempotence)
- [x] 4.3 Wrap `HotkeyService.Register`'s `HwndSource` construction in try/catch with logging (replace the dead zero-handle branch)

## 5. Modal re-entrancy guard

- [x] 5.1 Add a `_closePromptOpen` guard in `ContainerWindow` (pattern: `_pickerOpen` in `App.xaml.cs`); defer `EmptiedByPopOut`-driven `Close()` and picker requests until the prompt returns
- [x] 5.2 Re-check `_viewModel.Tabs.Count` after the `MessageBox` returns before executing the Yes path

## 6. VM leak on open failure

- [x] 6.1 try/catch around `new ContainerWindow(...)` / `Show()` in `App.OpenContainer`; call `vm.Detach()` on failure

## 7. Validation

- [x] 7.1 `dotnet build TabDock.sln` clean
- [x] 7.2 ValidationDriver: crashkill-rescue scenario passes; normal capture/release unaffected
