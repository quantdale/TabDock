# Win32 interop contract audit — 2026-08-13

## Scope and result

This is the focused second-order audit required after the historical
`DeferWindowPos` ABI defect. It covers every declaration in the production
`NativeMethods.cs`, including declarations linked into the ValidationDriver and
marked `DRIVER-ONLY`. The experimental Spike has separate demonstration
imports and is not a production path.

The audit found and fixed these contract discrepancies:

1. `DeferWindowPos` returned `BOOL` instead of the pointer-sized `HDWP`, and
   the split transaction discarded the returned handle. The declaration is
   now `IntPtr`; every append carries the returned handle forward and NULL or
   `EndDeferWindowPos` failure abandons the batch and uses the non-atomic
   fallback.
2. `ShowWindow` was declared with `SetLastError=true` and callers treated its
   BOOL as operation success. The import no longer captures last error and
   production callers verify visibility/iconic/zoomed postconditions instead.
3. `WINDOWPLACEMENT` omitted the SDK's trailing `rcDevice` field and
   `GetWindowPlacement` was declared with `out`, which discarded the required
   initialized `length`. The struct now has the SDK field order and the import
   uses `ref`; callers initialize `length` to `Marshal.SizeOf<WINDOWPLACEMENT>()`.
4. Process and text imports that depend on Unicode are explicit W entry points:
   `Process32FirstW`, `Process32NextW`, `GetWindowTextLengthW`,
   `GetWindowLongPtrW`, and `SetWindowLongPtrW`.
5. Production no longer declares or calls `DwmSetWindowAttribute`. A
   cross-process cosmetic mutation cannot be restored after a hard kill; the
   remaining `DwmGetWindowAttribute` calls are read-only diagnostics/picker
   observations.

The deterministic diagnostic self-test checks the HDWP return type, the
`WINDOWPLACEMENT` by-reference shape/size/`rcDevice`, the ShowWindow import
flags, and the stable capture identity predicate.

## Official contract references

- [`BeginDeferWindowPos`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-begindeferwindowpos)
  returns an `HDWP` and returns NULL on failure.
- [`DeferWindowPos`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-deferwindowpos)
  returns an updated `HDWP`, which may differ from the input; NULL abandons the
  transaction and the failed chain must not be passed to End.
- [`EndDeferWindowPos`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enddeferwindowpos)
  returns BOOL and takes the most recently returned `HDWP`.
- [`ShowWindow`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-showwindow)
  returns the window's previous visibility state, not operation success.
- [`GetWindowPlacement`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowplacement)
  and [`WINDOWPLACEMENT`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-windowplacement)
  require the caller to initialize `length`; the SDK struct includes `rcDevice`.
- [`GetWindowLongPtrW`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowlongptrw)
  and SetWindowLongPtrW use pointer-sized `LONG_PTR` values.
- [`SetWinEventHook`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwineventhook)
  returns an `HWINEVENTHOOK`, requires a message loop for out-of-context
  delivery, and requires a rooted callback; [`UnhookWinEvent`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-unhookwinevent)
  must run on the installing thread and returns BOOL.
- [`SendMessageTimeoutW`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendmessagetimeoutw)
  returns pointer-sized `LRESULT`, writes pointer-sized `DWORD_PTR`, can time
  out without setting last error, and is bounded in production.
- [`Process32FirstW`](https://learn.microsoft.com/en-us/windows/win32/api/tlhelp32/nf-tlhelp32-process32firstw)
  requires `PROCESSENTRY32.dwSize` to be initialized and returns BOOL.

## Declaration inventory

### user32.dll — production window, message, and geometry surface

| Import(s) | Contract disposition |
| --- | --- |
| `EnumWindows`, `EnumChildWindows` | BOOL callback enumeration; callback is a managed delegate held for the duration of the call. `EnumChildWindows` is DRIVER-ONLY. |
| `GetWindowThreadProcessId` | DWORD return/PID out parameter represented as `uint`; zero PID is handled as invalid. |
| `IsWindow`, `IsWindowVisible`, `IsWindowEnabled`, `IsIconic`, `IsZoomed` | BOOL observations; no last-error interpretation. |
| `GetWindowRect`, `GetClientRect`, `ClientToScreen` | BOOL plus `RECT`/`POINT` structures with four-byte LONG fields; failure is checked where the result matters. |
| `SetWindowPos` | BOOL; HWNDs and the `hWndInsertAfter` value are pointer-sized; failures capture last error immediately. |
| `FindWindow` | Unicode HWND lookup; only used with later ownership/identity gates. |
| `BeginDeferWindowPos`, `DeferWindowPos`, `EndDeferWindowPos` | `HDWP` is `IntPtr` for the first two; only End is BOOL. The split caller preserves the returned handle and abandons failed chains. |
| `GetParent`, `GetAncestor`, `GetWindow` | HWND returns and HWND parameters are `IntPtr`; command constants are `uint`. `GetParent` is DRIVER-ONLY. |
| `GetWindowLongPtrW`, `SetWindowLongPtrW` | Explicit W entry points; `LONG_PTR` is `nint`; index is signed `int`. Production only observes styles and does not mutate guest styles. |
| `GetWindowText`, `GetClassName`, `GetWindowTextLengthW` | Unicode text buffers with signed `int` lengths; title reads are metadata, not ongoing HWND identity. |
| `RegisterClassEx`, `CreateWindowEx`, `DefWindowProc`, `DestroyWindow` | WNDCLASSEX uses pointer-sized handles/function pointer and a `uint` `cbSize`; class callback is rooted by `NativeHwndHost`; created marker HWND is destroyed. |
| `ShowWindow`, `UpdateWindow`, `DestroyIcon` | `ShowWindow` BOOL is prior visibility only and is postcondition-checked; `DestroyIcon` releases extracted icon handles. |
| `GetWindowPlacement`, `SetWindowPlacement` | BOOL and `ref WINDOWPLACEMENT`; callers set `length` before both calls. |
| `SetForegroundWindow`, `AllowSetForegroundWindow`, `GetForegroundWindow` | BOOL/ HWND contracts; foreground policy treats a false SetForegroundWindow as a request that must be verified by a foreground observation. `AllowSetForegroundWindow` is DRIVER-ONLY and accepts the signed `ASFW_ANY` sentinel. |
| `WindowFromPoint` | HWND observation; DRIVER-ONLY. |
| `SetWinEventHook`, `UnhookWinEvent` | Pointer-sized hook handle; callback is stored in `WinEventMonitor._callback`; installation is all-or-nothing with bounded cleanup/retry, and teardown is UI-thread-affine. |
| `RegisterHotKey`, `UnregisterHotKey` | BOOL registration contracts; registration is checked/logged and teardown is best effort. |
| `GetDC`, `ReleaseDC`, `PrintWindow` | DRIVER-ONLY; screen DC is released with `ReleaseDC`, and PrintWindow BOOL is checked. |
| `PostMessage` | BOOL asynchronous post; callers identity-check the target and capture last error only for a false return. |
| `SendMessageTimeout` | Pointer-sized `LRESULT` return and `out IntPtr` for `PDWORD_PTR`; `SMTO_ABORTIFHUNG` and a 500 ms bound prevent a hung guest blocking the UI. A zero result is treated as timeout/failure, not decoded through stale last error. |
| `PostQuitMessage`, `TranslateMessage`, `DispatchMessage`, `GetDesktopWindow` | Standard message-loop/desktop HWND contracts; no ownership cleanup required. |
| `GetSystemMetrics`, `LoadIcon`, `LoadCursor`, `MonitorFromWindow` | Signed metrics and pointer-sized handle returns; `LoadCursor`/stock resources are not destroyed by TabDock. |
| `GetDpiForWindow`, `GetDpiForSystem`, `GetWindowDpiAwarenessContext`, `GetAwarenessFromDpiAwarenessContext`, `AreDpiAwarenessContextsEqual`, `SetProcessDpiAwarenessContext` | UINT/context/BOOL contracts; zero/null probe results are treated as unavailable or fail-closed where geometry correctness depends on them. Process DPI context is set at startup before WPF windows. |
| `GetMonitorInfo`, `EnumDisplayMonitors`, `EnumDisplayDevices` | `MONITORINFO.cbSize` and `DISPLAY_DEVICE.cb` are initialized as DWORD `uint`; monitor callback delegate remains rooted for the synchronous call. |
| `SetCursorPos`, `GetCursorPos`, `SendInput` | DRIVER-ONLY real-input surface. Coordinates are signed LONGs; `SendInput` returns count sent and callers use `Marshal.SizeOf<INPUT>()`; safety requires identity gates and supervised execution. |

### shcore.dll

`GetDpiForMonitor` returns an HRESULT (`int`), with `out uint` effective DPI
values. Callers compare the HRESULT to `S_OK` and never interpret a failed DPI
value as 96 DPI. Its documented DPI-awareness limitations are contained by the
existing per-monitor-v2/effective-DPI policy and the fail-closed capture probes.

### kernel32.dll

`AttachConsole`/`FreeConsole` are BOOL setup calls. `GetModuleHandle` and
`OpenProcess` return pointer-sized handles; `OpenProcess` uses BOOL inheritance
and the limited-information access mask. Every successful process handle is
closed in a `finally` block. `CloseHandle` is BOOL and is used for process,
token, and Toolhelp snapshot handles.

`QueryFullProcessImageName` is the Unicode API with a `StringBuilder` and
`ref uint` character capacity. The helper grows the buffer to the documented
Windows path bound and captures last error before returning. `GetLastError`,
`GetCurrentProcessId`, and `GetCurrentThreadId` return DWORD values represented
as `uint`.

`CreateToolhelp32Snapshot` returns a handle or `INVALID_HANDLE_VALUE`, and the
driver checks both zero and `-1`. `PROCESSENTRY32` uses pointer-sized
`th32DefaultHeapID`, DWORD fields, a 260-character Unicode executable name,
and initializes `dwSize`; `Process32FirstW`/`Process32NextW` are explicit W
imports. The snapshot is always closed.

### advapi32.dll

`OpenProcessToken` returns BOOL and an owned token `HANDLE`; the token is
closed in a `finally` block. `GetTokenInformation` uses the enum and DWORD
buffer lengths expected by the SDK. The two-call size/query pattern captures
failure immediately and frees its unmanaged buffer.

### shell32.dll

`ExtractIconEx` is the Unicode API with signed icon index, UINT count, and
owned output HICONs. `IconService` destroys both large and small returned icon
handles on success, no-icon, and exception paths.

### gdi32.dll — production icon-independent capture helpers and DRIVER-only pixels

`CreateCompatibleDC`, `CreateCompatibleBitmap`, `CreateDIBSection`,
`SelectObject`, `DeleteObject`, `CreateSolidBrush`, `DeleteDC`, `GetObject`,
`GetPixel`, and DRIVER-ONLY `BitBlt` use pointer-sized HDC/HGDIOBJ values and
the SDK's four-byte bitmap fields. The ValidationDriver releases screen DCs,
memory DCs, and bitmaps on all failure paths. The native host's class brush is
transferred to the system only after successful registration and is deleted on
registration failure/collision.

### dwmapi.dll

Only `DwmGetWindowAttribute` remains in production. It returns an HRESULT and
the current callers use it for BOOL-sized cloaking/transition observations,
checking `hr == 0`. There is intentionally no production `DwmSetWindowAttribute`
declaration: a previous-value snapshot in memory would not survive a hard
TabDock termination.

## Boundary and lifecycle review

- No production import uses a non-default calling convention; all listed APIs
  are Win32 `WINAPI` and the .NET default is correct.
- Pointer-sized handles, HWNDs, `LRESULT`, `LONG_PTR`, `WPARAM`/`LPARAM`,
  `DWORD_PTR`, and `ULONG_PTR` fields are represented by `IntPtr`/`nint`.
- BOOL returns are represented by managed `bool` only where the API really
  returns BOOL. State-return APIs (`ShowWindow`, `SendInput` count,
  `GetDpiForWindow` zero, HRESULT-returning DPI/DWM calls) are handled by their
  documented semantics.
- WinEvent callbacks are rooted in the monitor instance; posted UI callbacks
  carry the callback-time member reference and revalidate it before mutation.
  Hook installation, same-thread unhook, dispatcher stop, and residual-handle
  cleanup were rechecked in `WinEventMonitor`.
- Cross-process operations that can block are limited to the bounded
  `SendMessageTimeout` min-track probe. UIPI/elevation gates are preserved.
- Handles with explicit ownership (`OpenProcess`, token, snapshot, DC, HICON,
  GDI objects, WinEvent hooks) have corresponding cleanup paths. Stock cursors,
  icons, desktop HWNDs, and borrowed window handles are not destroyed.
- The managed structs were checked for field order and width: `RECT`, `POINT`,
  `MINMAXINFO`, `INPUT`/union, `MSG`, `WNDCLASSEX`, `WINDOWPLACEMENT`,
  `PROCESSENTRY32`, `BITMAP*`, `MONITORINFO`, `DISPLAY_DEVICE`, and
  `TOKEN_ELEVATION`. The diagnostic self-test asserts the critical
  `WINDOWPLACEMENT` size of 60 bytes.
- The imports marked DRIVER-ONLY are not reachable from production behavior;
  they remain centralized because the ValidationDriver links the same native
  contract file.

## Remaining limitations

Native fault injection for a failed `BeginDeferWindowPos` append, failed
`EndDeferWindowPos`, partial WinEvent installation, and UIPI-denied operations
is not safe in hosted CI. The production fallback and cleanup state machines
are covered by source-level guards, deterministic self-tests, and the targeted
supervised real-HWND scenarios recorded in `.agent/STATE.md`; direct native
fault injection remains an unclaimed limitation rather than a fabricated pass.
