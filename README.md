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
- `Services/WindowShepherdService.cs` — TabDock's only capture backend. Positions/shows/hides a captured window over the container's content area without ever reparenting or restyling it; also owns the crash-recovery journal for hidden guests.
- `Services/WinEventMonitor.cs` — out-of-process `SetWinEventHook` wrapper.
- `Services/GroupManager.cs` — owns all groups and enforces the flat, no-nesting rule.
- `Services/PersistenceService.cs` — JSON persistence of group metadata.
- `Services/LoggingService.cs` — rotating log in `%APPDATA%\TabDock\logs\`.
- `Infrastructure/NativeHwndHost.cs` — a plain `HwndHost` marker sized to match the WPF-rendered content area; captured windows are positioned over it, never reparented into it.
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

18. Group several windows, with at least one on an inactive tab.
19. Kill `TabDock.exe` from Task Manager (`taskkill /F /IM TabDock.exe`).
20. Verify every captured window/process is still running — since none of them were ever reparented, killing TabDock can no longer destroy them. The window that was on the active tab is left wherever it was positioned; relaunch TabDock and verify the window that was on an inactive (hidden) tab reappears automatically (the crash-recovery journal restores it).

### DPI change

21. Move the container between monitors with different scaling (e.g., 100% and 150%).
22. Verify the content area re-lays out and the active window fills the host.

### Maximize / restore

23. Maximize the container with a window docked; verify the docked window resizes to fill the whole content area.
24. Restore the container; verify the docked window shrinks back to match.

## Known limitations

- **Guest self-maximize:** if you maximize the docked window itself (not the container), it fills the whole monitor, breaking the docked look — there's no reliable signal that distinguishes this from an ordinary interactive resize, so nothing corrects it automatically. Not a rendering or input bug, just a cosmetic gap.

- **Elevated windows:** A non-elevated TabDock cannot capture a window owned by an elevated process due to UIPI (it can't position/foreground it either, not just reparent it). TabDock ships as a standard-user app and asks the user to run elevated if they need to group elevated windows.

- **Persistence across reboots:** HWNDs are not stable across reboots, so TabDock cannot reliably re-attach the exact same live windows after a restart. It persists group names, accent colors, custom labels, tab order, and executable paths as layout intent. On startup it restores the group definitions but leaves them empty for the user to re-populate. It never persists application content.

- **Task Manager kill:** captured windows are never reparented, so force-killing TabDock (`taskkill /F`) no longer destroys them — a strict improvement over earlier versions. A window that was on an inactive (hidden) tab at the moment of the kill has no way to reappear on its own; TabDock journals hides to `%APPDATA%\TabDock\hidden-windows.json` and restores any still-valid entry the next time it starts.

## Logging

Diagnostic logs are written to:

```
%APPDATA%\TabDock\logs\TabDock.log
```

Log rotation keeps the current file under 1 MB; older logs are moved to `TabDock.log.old`.

## License

This is a reference implementation. Use and modify at your own risk.
# TabDock
