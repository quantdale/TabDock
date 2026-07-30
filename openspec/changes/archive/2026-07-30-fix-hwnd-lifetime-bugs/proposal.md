## Why

A full-codebase review found several defects around HWND lifetime and Windows' aggressive HWND-value recycling:

- Container HWND unregistration in `ContainerWindow_Closed` (`Views/ContainerWindow.xaml.cs:304-307`) is ineffective: both reads (`WindowInteropHelper.Handle`, `NativeHwndHost.HostWindowHandle`) return `IntPtr.Zero` by `Closed` time, so the `UnregisterContainerHwnd` calls no-op and every closed container leaks two stale HWND values into `GroupManager._ownContainerHwnds`. When Windows recycles one of those values for an unrelated window, `IsOwnWindow` (`Services/GroupManager.cs:129`) refuses to capture it as "no nesting".
- `Release()` can hide the released guest forever when capture-time `GetWindowPlacement` failed (`Services/WindowShepherdService.cs:97-101`): the zeroed `showCmd` (0 == `SW_HIDE`) is passed to `ShowWindow` at `:303`, and the journal is cleared, so crash recovery can't restore the vanished window.
- `Hide()` (`:185-192`) writes the crash-recovery journal *after* `SW_HIDE`, and `Release(show:false)` (`:269-285`) issues a redundant `SW_HIDE` before its `JournalClear`; a force-kill in between leaves an unrescuable hidden orphan (or a stale journal entry rescue would wrongly un-show).
- `GroupViewModel.OnCloseWindowRequested` (`ViewModels/GroupViewModel.cs:246-259`) and the container close-Yes path have an `IsWindow`-then-`PostMessage(WM_CLOSE)` TOCTOU: a recycled HWND could deliver `WM_CLOSE` to an arbitrary third-party window, and `PostMessage` failure (UIPI) is silently ignored.
- `WinEventMonitor.Stop()` zeroes hook handles without checking `UnhookWinEvent` (leak on failure), doesn't drain posted callbacks or re-check capture membership at dispatch time (HWND-recycle race between native event and posted dispatch), and silently tolerates partial hook installation.

## What Changes

- **Container HWND unregister fix** — cache container + `NativeHwndHost` HWNDs into fields in `ContainerWindow_Loaded`; unregister the cached values in `Closed` (or unregister during `Closing`).
- **Release with invalid placement** — track `hasValidPlacement` on `CapturedWindow`; when capture-time `GetWindowPlacement` failed, skip `SetWindowPlacement`/`ShowWindow(showCmd)` and use `SW_SHOW` after restoring `OriginalBounds`. Remove the dead `HWND_TOP` argument from the fallback `SetWindowPos` (already `SWP_NOZORDER`).
- **Journal-before-hide ordering** — in `Hide()`, journal synchronously *before* `SW_HIDE` (safe: rescue tolerates already-visible windows per the `JournalClear` doc comment); in `Release(show:false)`, clear the journal before (or drop) the redundant `SW_HIDE`.
- **WM_CLOSE recycle guard** — immediately before `PostMessage(WM_CLOSE)`, verify `GetWindowThreadProcessId(hwnd)` equals the stored `CapturedWindow.ProcessId`; check `PostMessage`'s return and log `FormatLastError()` on failure.
- **WinEventMonitor hardening** — only zero a hook handle field when `UnhookWinEvent` returns true; null `_uiContext` in `Stop` and guard `Raise` with `_running` + re-check `_isCapturedWindow(args.Hwnd)` at dispatch time; verify all six `SetWinEventHook` results non-zero in `Start` (log/unwind on partial failure); handle null `SynchronizationContext.Current` explicitly.

## Capabilities

### New Capabilities
(none)

### Modified Capabilities
- `hidden-window-journal`: journal write ordering strengthened — entries are written before the window is hidden, and `Release` never hides a guest via an invalid placement.

## Impact

- **Code**: `Views/ContainerWindow.xaml.cs`, `Services/WindowShepherdService.cs`, `Services/WinEventMonitor.cs`, `ViewModels/GroupViewModel.cs`, `Models/CapturedWindow.cs`.
- **No API/dependency/schema changes.** Behavioral fixes only; journal on-disk shape unchanged.
- **Risk**: low — fixes are localized; the `Hide` journaling order is proven safe by the rescue path's documented tolerance for already-visible windows.
