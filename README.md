# TabDock

A universal window tab-grouping tool for Windows. Merge multiple independent application windows (browser, terminal, editor, etc.) into a single container window with a browser-style tab strip.

Built with C# / .NET 8 / WPF, using only P/Invoke for native interop.

## Requirements

- Windows 10 (recent builds) or Windows 11
- .NET 8 SDK (for development builds)
- Visual Studio 2022 or `dotnet` CLI

## Build and run

### Development build

```powershell
dotnet build TabDock.csproj
```

Run the app:

```powershell
.\bin\Debug\net8.0-windows\win-x64\TabDock.exe
```

### Publish a single-file executable

```powershell
dotnet publish TabDock.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:IncludeNativeLibrariesForSelfExtract=true
```

The resulting single executable is at:

```
.\bin\Release\net8.0-windows\win-x64\publish\TabDock.exe
```

### Notes on Native AOT

WPF relies on COM activation, reflection emit, and other runtime features that are incompatible with .NET Native AOT, so the publish profile uses a **self-contained single-file executable** instead. This still produces one distributable file with no external runtime dependency.

## How to use

1. Launch `TabDock.exe`. The main launcher window appears.
2. Click **New group** or press **Ctrl+Alt+G** to open the capture picker.
3. In the picker, select the windows you want to group and choose whether to add them to a new group or an existing one.
4. The container window shows a tab for each captured window. Click tabs to switch, drag tabs to reorder, or drag a tab out of the strip to release it back to a standalone window.
5. Double-click the group name in the title bar to rename it.
6. Click the colored chip in the title bar to change the group's accent color.
7. Closing a container asks whether to close the grouped applications or release them back to standalone windows.

## Architecture overview

- `NativeMethods.cs` — all P/Invoke declarations in one place.
- `Services/WindowCaptureService.cs` — captures, lays out, and releases external windows.
- `Services/WinEventMonitor.cs` — out-of-process `SetWinEventHook` wrapper.
- `Services/GroupManager.cs` — owns all groups and enforces the flat, no-nesting rule.
- `Services/PersistenceService.cs` — JSON persistence of group metadata.
- `Services/RenderHealthService.cs` — detects black/frozen GPU-rendered tabs and triggers recovery.
- `Services/LoggingService.cs` — rotating log in `%APPDATA%\TabDock\logs\`.
- `Infrastructure/NativeHwndHost.cs` — WPF `HwndHost` that hosts reparented windows.
- `Views/ContainerWindow.xaml` — custom chrome, tab strip, content host.
- `Views/CapturePickerWindow.xaml` — window picker for creating groups.

## Manual test checklist

Use this checklist to verify a build before considering it ready.

### Basic grouping

1. Open **Chrome**, **Windows Terminal**, and **Cursor** (or any editor) as separate windows.
2. Launch TabDock and press **Ctrl+Alt+G**.
3. Select the three windows in the picker and click **Group these**.
4. Verify the container opens with three tabs and the active window fills the content area.

### Tab switching and reordering

5. Click each tab; verify the correct window is shown and the others are hidden.
6. Drag a tab left/right in the strip; verify the order updates.

### Group identity

7. Double-click the group name and rename it to "Acme Corp - Invoice".
8. Click the colored chip and choose a different accent color; verify the title bar/tab highlight updates.

### Ungroup

9. Drag one tab out of the container; verify the window returns to a standalone window at its original size, position, and style (caption, borders, maximize button).
10. Right-click a tab and choose **Pop out**; verify the same.

### Close from inside a tab

11. Close one of the captured applications from its own UI (e.g., close Chrome).
12. Verify its tab disappears cleanly. If it was the last tab, verify the container closes.

### No nested groups

13. Try to capture an existing TabDock container window into another group (use the picker and select the TabDock window).
14. Verify the operation is refused with a clear message.

### Elevated windows

15. Open Windows Terminal as Administrator.
16. Try to capture it with TabDock running as a standard user.
17. Verify a clear message explains that elevated windows cannot be grouped unless TabDock is also run as administrator.

### Kill TabDock via Task Manager

18. Group several windows.
19. Kill `TabDock.exe` from Task Manager (`taskkill /F /IM TabDock.exe`).
20. Verify all captured windows survive and return to standalone windows.  
    *(Note: this relies on in-process cleanup. An abrupt process kill is an OS-level race; windows are released as part of process teardown whenever possible.)*

### DPI change

21. Move the container between monitors with different scaling (e.g., 100% and 150%).
22. Verify the content area re-lays out and the active window fills the host.

### GPU-render recovery

23. Capture Chrome without the `--disable-gpu` flag if possible, or capture Cursor.
24. If the embedded area is black/frozen, verify TabDock automatically releases that window back to standalone and shows a non-destructive notification.

## Known limitations

- **GPU-rendered / Electron / DirectX apps:** Chrome, Windows Terminal, and Electron-based editors (e.g., Cursor) use hardware-accelerated rendering. Reparenting sometimes produces a black or frozen window. TabDock detects this and releases the window back to standalone, but it cannot force every app to render correctly inside a foreign parent. Chrome can usually be embedded when started with `--disable-gpu`; Cursor did not render reliably even with GPU disabled in our tests.

- **Elevated windows:** A non-elevated TabDock cannot reparent a window owned by an elevated process due to UIPI. TabDock ships as a standard-user app and asks the user to run elevated if they need to group elevated windows.

- **DPI awareness:** `SetParent` can behave unexpectedly when the child and parent run under different DPI-awareness contexts. TabDock declares Per-Monitor-V2 awareness and re-lays out on DPI changes, but mixed-awareness apps may still show slight sizing or scaling issues.

- **Persistence across reboots:** HWNDs are not stable across reboots, so TabDock cannot reliably re-attach the exact same live windows after a restart. It persists group names, accent colors, custom labels, tab order, and executable paths as layout intent. On startup it restores the group definitions but leaves them empty for the user to re-populate. It never persists application content.

- **Task Manager kill:** If the TabDock process is force-killed, Windows destroys child HWNDs as part of cleaning up the dead parent's window tree. In-process cleanup runs for normal exit, unhandled exceptions, and session-ending events, but an abrupt `taskkill /F` cannot be intercepted with 100% reliability.

## Logging

Diagnostic logs are written to:

```
%APPDATA%\TabDock\logs\TabDock.log
```

Log rotation keeps the current file under 1 MB; older logs are moved to `TabDock.log.old`.

## License

This is a reference implementation. Use and modify at your own risk.
# TabDock
