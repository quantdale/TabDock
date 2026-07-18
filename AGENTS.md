# TabDock — Agent Guide

This file is a concise, accurate reference for AI coding agents working on the TabDock repository. It reflects the actual project contents; do not assume conventions that are not documented here.

---

## Project overview

TabDock is a Windows desktop utility that merges multiple independent application windows (browser, terminal, editor, etc.) into a single container window with a browser-style tab strip. It is implemented as a C# / .NET 8 / WPF application and uses only P/Invoke for native interop — no third-party NuGet packages.

A captured window is never reparented: TabDock positions it over the container's content area and z-orders it in place (the "Shepherd" model — see `WindowShepherdService` in the Services table below), so it remains, from Windows' point of view, an ordinary independent top-level window the whole time it's captured.

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
└── Spike/TabDock.Spike/              Experimental survival spike

tests/ValidationDriver/               Not in TabDock.sln — build/run by project path
├── TabDock.ValidationDriver/         Real-input (SendInput) validation harness
└── TabDock.GuineaPig/                Disposable WinForms target app it spawns
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
| `Infrastructure/` | `NativeHwndHost` — a plain `HwndHost` marker window sized/positioned to match the WPF-rendered content area; guests are positioned over it, never reparented into it |
| `Converters/` | `BoolToVisibilityConverter`, `ColorToBrushConverter` |
| `app.manifest` | DPI awareness, compatibility, and execution level (`asInvoker`) |

### Services

| Service | Responsibility |
|---------|----------------|
| `WindowShepherdService` | TabDock's only capture backend. Positions/shows/hides an external HWND over the container's content area via `SetWindowPos`/`ShowWindow` — never reparents or restyles it. Release restores the capture-time `WINDOWPLACEMENT`. Also owns the `hidden-windows.json` crash-recovery journal (see `RescueOrphanedWindows`) |
| `GroupManager` | Owns all groups; enforces flat, no-nesting rule; coordinates tab switching/reordering/release |
| `PersistenceService` | Saves/restores group metadata to `%APPDATA%\TabDock\state.json` |
| `WinEventMonitor` | Out-of-process `SetWinEventHook` wrapper for destroy/rename/minimize/foreground events on captured windows. Filters by direct member-HWND match — never by `GetAncestor`, which cannot see an already-destroyed window's ancestors |
| `HotkeyService` | Registers global `Ctrl+Alt+G` hotkey |
| `IconService` | Extracts executable icons for tab thumbnails |
| `LoggingService` | Rotating file logger in `%APPDATA%\TabDock\logs\TabDock.log` |

`WindowCaptureService` (`SetParent`-based reparenting), `RenderHealthService` (`PrintWindow`-based black-frame detection), `DpiService` (DPI-forwarding to a reparented child), and `GuestActivationHelper` (synthetic activation messages) were deleted together in the Shepherd migration — all four existed solely to compensate for problems that `SetParent` reparenting caused and that Shepherd's never-reparent model doesn't have in the first place.

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

### Automated real-input test

`tests/ValidationDriver/TabDock.ValidationDriver` is a console harness that drives a fresh TabDock instance plus guinea-pig/real-app windows entirely through synthesized `SendInput` mouse/keyboard at UIA-read coordinates, then asserts on window state, pixels, and log output:

```powershell
dotnet run --project tests\ValidationDriver\TabDock.ValidationDriver\TabDock.ValidationDriver.csproj -- --yes <scenario|all>
```

Since a Shepherd guest is never reparented, "is this guest captured/released" can no longer be read off `WS_CHILD`/`GetParent` (both are permanently unchanged) — scenarios instead compare the guest's `GetWindowRect` against the container's content-area marker (`IsDocked`/`IsReleasedAndShown`/`IsReleasedAndHidden` helpers in `Scenarios.cs`). Notable scenarios beyond the general capture/release/tab-switch coverage: `dragout-by-titlebar` (drag the guest's own native title bar past/under the pop-out threshold), `directclick-foreground-pairing` (click the guest directly, bypassing TabDock's own UI, and verify z-order re-pairing), `crashkill-rescue` (force-kill TabDock with a hidden tab captured, relaunch, verify the crash-recovery journal restores it), `realapp-multi-render` (real apps, `PrintWindow`-verified live rendering, byte-identical placement/style/exstyle/parent before vs. after capture+release — this replaced the old, Reparent-only `tests/CaptureReleaseTest` project, since a `PrintWindow` capture of a GPU-rendered guest reads its own back-buffer directly and isn't affected by whatever else is on top of it on screen, unlike a `BitBlt`-based screen-region capture), and `keyboardinput-*-altswitch` (the direct regression test for the originally-reported "keyboard input stops after switching to another app and back" bug).

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
- `WindowShepherdService.Capture` rejects captures where the target PID equals the current process ID.
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

- **Guest self-maximize is a cosmetic gap.** If the user maximizes the docked guest itself (not via TabDock's own maximize), it fills the whole monitor, breaking the docked look — there is no reliable WinEvent signal that distinguishes a programmatic/self-maximize from the interactive move/size loop, so nothing corrects it. Not an input-correctness bug; out of scope for now.
- **Elevated windows** cannot be captured by a non-elevated TabDock instance.
- **Task Manager force-kill:** captured guest processes/windows now survive a `taskkill /F` against TabDock (they were never reparented into its window tree) — a hidden (inactive-tab) guest is restored on the next launch via the crash-recovery journal (`WindowShepherdService.RescueOrphanedWindows`); the previously-active guest is left wherever it was, unmanaged, until the next capture.

These limitations are documented in `README.md` and should not be treated as bugs to be fixed without changing the project's scope. GPU-rendered/Electron/DirectX apps showing black or frozen content, and mixed-DPI sizing issues across monitors, were artifacts of the deleted Reparent backend (`SetParent` breaking DWM composition and per-monitor DPI messages respectively) and no longer apply under Shepherd — a guest is never reparented, so it's never compositor-invalidated and always receives its own native DPI handling untouched.

---

## Useful references

- `README.md` — user-facing documentation, manual test checklist, and known limitations.
- `docs/internal/guarded-spawn-pattern.md` — mandatory guardrails for any process-spawning code.
- `NativeMethods.cs` — authoritative reference for all native interop used by the project.
