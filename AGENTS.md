# TabDock — Agent Guide

This file is a concise, accurate reference for AI coding agents working on the TabDock repository. It reflects the actual project contents; do not assume conventions that are not documented here.

---

## Project overview

TabDock is a Windows desktop utility that merges multiple independent application windows (browser, terminal, editor, etc.) into a single container window with a browser-style tab strip. It is implemented as a C# / .NET 8 / WPF application and uses only P/Invoke for native interop — no third-party NuGet packages.

Key design constraints:

- **No nested groups.** TabDock refuses to capture its own windows or any already-captured container.
- **Standard-user app.** The application manifest requests `asInvoker`; elevated windows can only be grouped if TabDock itself is run as administrator.
- **HWNDs are not persisted across reboots.** Persistence stores group metadata (name, accent color, tab order, executable paths) as layout intent only.
- **Emergency release.** Captured windows are released back to standalone on normal exit, unhandled exceptions, and dispatcher crashes whenever possible.

---

## Technology stack

| Layer | Technology |
|-------|------------|
| Language | C# 12 |
| Runtime | .NET 8 (`net8.0-windows`) |
| UI framework | WPF (Windows Presentation Foundation) |
| Native interop | P/Invoke to `user32.dll`, `kernel32.dll`, `advapi32.dll`, `shell32.dll`, `gdi32.dll`, `dwmapi.dll` |
| Serialization | System.Text.Json with source-generated `TabDockJsonContext` |
| Build system | SDK-style MSBuild project files, Visual Studio 2022 / `dotnet` CLI |
| Target platform | Windows 10/11, x64 (primary RID `win-x64`) |

The main project disables implicit usings and enables nullable reference types:

```xml
<Nullable>enable</Nullable>
<ImplicitUsings>disable</ImplicitUsings>
```

---

## Solution structure

```
TabDock.sln
├── TabDock.csproj                    Main WPF application
├── tests/CaptureReleaseTest/         End-to-end capture/release test harness
│   └── TabDock.CaptureReleaseTest/
└── Spike/TabDock.Spike/              Experimental survival spike
```

### Main project code organization

| Path | Responsibility |
|------|----------------|
| `App.xaml` / `App.xaml.cs` | Application entry point, service lifetime, global hotkey, container management, emergency release |
| `NativeMethods.cs` | **All** P/Invoke declarations, native structs, constants, and helper wrappers |
| `Services/` | Core business logic (see below) |
| `Models/` | Data objects: `CapturedWindow`, `Group`, `PersistedState`, persistence DTOs |
| `ViewModels/` | WPF view models and `RelayCommand` |
| `Views/` | WPF windows and dialogs (`MainWindow`, `ContainerWindow`, `CapturePickerWindow`) |
| `Infrastructure/` | `NativeHwndHost` — the WPF `HwndHost` that owns the native HWND for reparented windows |
| `Converters/` | `BoolToVisibilityConverter`, `ColorToBrushConverter` |
| `app.manifest` | DPI awareness, compatibility, and execution level (`asInvoker`) |

### Services

| Service | Responsibility |
|---------|----------------|
| `WindowCaptureService` | Reparent an external HWND into a TabDock host and restore it on release |
| `GroupManager` | Owns all groups; enforces flat, no-nesting rule; coordinates tab switching/reordering/release |
| `PersistenceService` | Saves/restores group metadata to `%APPDATA%\TabDock\state.json` |
| `WinEventMonitor` | Out-of-process `SetWinEventHook` wrapper for destroy/rename/minimize/foreground events on captured windows. Filters by direct member-HWND match — never by `GetAncestor`, which cannot see reparented children or already-destroyed windows |
| `HotkeyService` | Registers global `Ctrl+Alt+G` hotkey |
| `RenderHealthService` | Detects black/frozen GPU-rendered tabs using `PrintWindow` |
| `DpiService` | DPI awareness and scale-factor helpers |
| `IconService` | Extracts executable icons for tab thumbnails |
| `LoggingService` | Rotating file logger in `%APPDATA%\TabDock\logs\TabDock.log` |

---

## Build and run commands

### Development build

```powershell
dotnet build TabDock.csproj
```

Run the app:

```powershell
.\bin\Debug\net8.0-windows\win-x64\TabDock.exe
```

### Build the full solution

```powershell
dotnet build TabDock.sln
```

### Publish a single-file executable

```powershell
dotnet publish TabDock.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Output:

```
.\bin\Release\net8.0-windows\win-x64\publish\TabDock.exe
```

This produces one distributable file with no external runtime dependency. Native AOT is intentionally **not** used because WPF relies on COM activation, reflection emit, and other runtime features incompatible with trimming/AOT.

---

## Testing instructions

### Automated end-to-end test

`tests/CaptureReleaseTest/TabDock.CaptureReleaseTest` is a console harness that:

1. Spawns Paint, Windows Terminal, Chrome, and Cursor (when available).
2. Captures each into a native host window.
3. Verifies live rendering by comparing two screen-captured frames.
4. Releases the window and compares HWND state (parent, style, ex-style, bounds, placement) against a pre-capture snapshot.

Run it:

```powershell
dotnet run --project tests\CaptureReleaseTest\TabDock.CaptureReleaseTest\TabDock.CaptureReleaseTest.csproj
```

The test requires interactive confirmation, spawns real applications, and kills them on completion or failure. Do not run it unattended on a production machine.

### Manual test checklist

The `README.md` contains a detailed manual checklist covering:

- Basic grouping via the capture picker (`Ctrl+Alt+G`)
- Tab switching and drag-to-reorder
- Group renaming and accent-color changes
- Drag-out / right-click **Pop out** release
- Closing a captured application from its own UI
- Refusing nested groups
- Elevated-window UIPI handling
- Force-kill via Task Manager
- DPI changes across monitors
- GPU-render recovery

Use this checklist before considering a build ready for use.

### Survival spike

`Spike/TabDock.Spike` is an experimental console app that reparents a Command Prompt into a throwaway host and then force-kills the host to observe whether the child HWND survives. It is not part of normal CI; run it only when investigating OS-level reparenting behavior.

---

## Code style guidelines

- **Implicit usings are disabled.** Every source file must include explicit `using` directives.
- **Nullable reference types are enabled.** Mark nullable reference types with `?`.
- **XML doc comments** are expected on public types and non-trivial members, especially in `Services/` and `Models/`.
- **P/Invoke lives in one file only.** Add new native declarations to `NativeMethods.cs`; do not scatter them across the codebase.
- **File-scoped namespaces** are not used; the project uses classic block namespaces (`namespace TabDock;`).
- **Naming** follows standard .NET conventions: PascalCase for types/members, camelCase for locals/parameters, `_camelCase` for private fields.
- **Null-forgiving operator** (`!`) is used sparingly where the compiler cannot prove non-null (e.g., service fields initialized in `Application_Startup`).

---

## Development conventions

### Guarded process-spawn pattern

Any code that calls `Process.Start` must follow the guardrails in `docs/internal/guarded-spawn-pattern.md`:

1. Hard spawn cap enforced by a counter and lock.
2. No bare retry loops; use explicit `maxRetries` and abort on cap.
3. Named mutex for single-instance standalone tools.
4. Track spawned processes and kill them on exit/timeout/Ctrl+C.
5. Hard overall timeout via `CancellationTokenSource`.
6. Visible, flushed console logging for every spawn/check/kill.
7. Manual confirmation for one-off destructive tests.

The pattern was made mandatory after a runaway self-recursion incident in `Spike/TabDock.Spike`.

### Window ownership and no-nesting rule

- `GroupManager.IsOwnWindow` checks process ID and a registered set of container HWNDs.
- `WindowCaptureService` rejects captures where the target PID equals the current process ID.
- Container windows register their own HWND and the `NativeHwndHost` HWND with `GroupManager`.

### Error handling

- Native errors are logged via `LoggingService` with `NativeMethods.FormatLastError()`.
- Render-health failures and window teardown are best-effort and must not crash the container.
- `LoggingService` itself is fail-safe; it catches and suppresses its own exceptions.

---

## Security considerations

- **Elevation:** TabDock ships as a standard-user application. It detects elevated target processes and rejects the capture with a clear message rather than auto-elevating itself.
- **UIPI:** Capturing a window owned by a higher-integrity process is blocked by `SetParent` / `OpenProcessToken` checks.
- **No external dependencies:** The project does not reference third-party NuGet packages, minimizing supply-chain surface area.
- **Persistence:** Only metadata is written to `%APPDATA%\TabDock\state.json`. No application content, credentials, or HWNDs are persisted.
- **Logs:** Diagnostic logs are written to `%APPDATA%\TabDock\logs\TabDock.log` and rotated at 1 MB.

---

## Deployment

The intended distribution artifact is the self-contained single-file executable produced by `dotnet publish` (see Build commands above). There is no installer or MSIX package. The published executable has no external runtime dependency on the target machine.

---

## Known limitations

- **GPU-rendered / Electron / DirectX apps** may show black or frozen content when reparented. TabDock detects this and releases the window back to standalone.
- **Elevated windows** cannot be captured by a non-elevated TabDock instance.
- **Mixed DPI awareness** can cause slight sizing issues when moving containers between monitors with different scaling.
- **Task Manager force-kill:** A `taskkill /F` against TabDock cannot be intercepted reliably; child HWNDs may be destroyed as part of the dead parent's window tree.

These limitations are documented in `README.md` and should not be treated as bugs to be fixed without changing the project's scope.

---

## Useful references

- `README.md` — user-facing documentation, manual test checklist, and known limitations.
- `docs/internal/guarded-spawn-pattern.md` — mandatory guardrails for any process-spawning code.
- `NativeMethods.cs` — authoritative reference for all native interop used by the project.
