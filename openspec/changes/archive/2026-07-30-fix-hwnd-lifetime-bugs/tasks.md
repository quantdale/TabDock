# Tasks

## 1. Container HWND unregistration

- [x] 1.1 Cache container HWND and `NativeHwndHost.HostWindowHandle` into private fields in `ContainerWindow_Loaded` (where both are known non-zero)
- [x] 1.2 Unregister the cached values in `ContainerWindow_Closed` (or move unregistration to `Closing`)
- [x] 1.3 Verify `GroupManager.UnregisterContainerHwnd(IntPtr.Zero)` remains a safe no-op

## 2. Release with invalid capture-time placement

- [x] 2.1 Add `hasValidPlacement` tracking to `CapturedWindow` (set from the `GetWindowPlacement` result in `Capture`)
- [x] 2.2 In `Release`, skip `SetWindowPlacement`/`ShowWindow(placement.showCmd)` when invalid; use `SW_SHOW` after the `OriginalBounds` `SetWindowPos`
- [x] 2.3 Remove the dead `HWND_TOP` argument from the fallback `SetWindowPos` (already `SWP_NOZORDER`)

## 3. Journal-before-hide ordering

- [x] 3.1 In `Hide()`, call `JournalHide` synchronously before `ShowWindow(SW_HIDE)`
- [x] 3.2 In `Release(show:false)`, `JournalClear` before the redundant `SW_HIDE` (or remove that `SW_HIDE`)

## 4. WM_CLOSE HWND-recycle guard

- [x] 4.1 In `GroupViewModel.OnCloseWindowRequested`, verify `GetWindowThreadProcessId(hwnd) == tab.Model.ProcessId` immediately before `PostMessage`
- [x] 4.2 Same check in `ContainerWindow` close-Yes path (`hwndsToClose` loop)
- [x] 4.3 Check `PostMessage` return value and log `FormatLastError()` on failure

## 5. WinEventMonitor hardening

- [x] 5.1 Only zero each hook field when `UnhookWinEvent` returns true; log `FormatLastError()` on failure
- [x] 5.2 Null `_uiContext` in `Stop`; guard `Raise` with `_running` and re-check `_isCapturedWindow(args.Hwnd)` at dispatch time
- [x] 5.3 In `Start`, verify all six hooks installed; log and unwind on partial failure
- [x] 5.4 Handle a null `SynchronizationContext.Current` in `Start` explicitly (log and refuse to start, or capture a dispatcher context in the constructor) instead of silently relying on `OnWinEvent`'s callback-thread `Raise` fallback, which breaks UI-thread affinity

## 6. Validation

- [x] 6.1 `dotnet build TabDock.sln` clean
- [x] 6.2 Run ValidationDriver and confirm capture/release/tab-switch/crashkill-rescue scenarios pass
