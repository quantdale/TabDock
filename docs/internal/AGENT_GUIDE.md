Exit code: 0
Wall time: 0.7 seconds
Output:
# TabDock — Detailed Agent Reference

This is the detailed, progressively-loaded reference for AI coding agents working on the TabDock repository. The compact, canonical entrypoint is the root `AGENTS.md`; load this file when a task needs the project’s detailed architecture, testing, safety, or coding rules. It reflects the actual project contents; do not assume conventions that are not documented here.

---

## Project overview

TabDock is a Windows desktop utility that merges multiple independent application windows (browser, terminal, editor, etc.) into a single container window with a browser-style tab strip. It is implemented as a C# / .NET 8 / WPF application and uses P/Invoke for native interop plus the stable Microsoft-maintained `System.Threading.AccessControl` package for the secured product-mutation mutex; it has no unrelated third-party NuGet packages.

A captured window is never reparented: TabDock positions it over the container's content area and z-orders it in place (the "Shepherd" model — see `WindowShepherdService` in the Services table below), so it remains, from Windows' point of view, an ordinary independent top-level window the whole time it's captured.

Key design constraints:

- **No nested groups.** TabDock refuses to capture its own windows or any already-captured container.
- **Standard-user app.** The application manifest requests `asInvoker`; elevated windows can only be grouped if TabDock itself is run as administrator.
- **HWNDs are not persisted across reboots.** Persistence stores group metadata (name, accent color, tab order, executable paths) as layout intent only.
- **Emergency release.** Captured windows are released back to standalone on normal exit, unhandled exceptions, and dispatcher crashes whenever possible.

### Repository navigation

Use the project-local Repowise MCP index for architecture discovery, dependency/caller relationships, history, implementation locations, and broad repository exploration. After it identifies relevant files or symbols, read the actual source directly before changing anything. Do not use the index as a substitute for source verification; fall back to normal repository tools when the index is stale or lacks the needed detail.
If `.repowise/` is absent, initialize it with `repowise init --no-prose --yes`; if its indexed commit is stale, refresh it with `repowise update --index-only`.

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

`TabDock.csproj` defaults `<NuGetAudit>false</NuGetAudit>` only for reliable
offline/local development. CI and the Release qualification script override
that property with `NuGetAudit=true`, `NuGetAuditMode=all`, and vulnerability
warnings treated as errors. Re-run the audited Release path before every
release and whenever a dependency is added.

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

The main csproj excludes `bin/**`, `obj/**`, `Spike/**`, `tests/**`, and `docs/**` from its default item globs. The ValidationDriver project compiles the main project's `NativeMethods.cs` into itself via a `<Compile Include="..\..\..\NativeMethods.cs" Link="..."/>` item — edits to `NativeMethods.cs` affect both.

### Main project code organization

| Path | Responsibility |
|------|----------------|
| `App.xaml` / `App.xaml.cs` | Application entry point, service lifetime, global hotkey, container management, emergency release. WinEvent-driven guest lifecycle is wired in with one `GuestLifecycleService.Attach` call |
| `NativeMethods.cs` | **All** P/Invoke declarations, native structs, constants, and helper wrappers |
| `Services/` | Core business logic (see below) |
| `Models/` | Data objects: `CapturedWindow`, `Group`, `PersistedState`, persistence DTOs |
| `ViewModels/` | WPF view models and `RelayCommand` |
| `Views/` | WPF windows and dialogs (`MainWindow`, `ContainerWindow`, fallback `CapturePickerWindow`); group switching and routine capture are in-window container surfaces |
| `Infrastructure/` | `NativeHwndHost` — a plain `HwndHost` marker window sized/positioned to match the WPF-rendered content area; guests are positioned over it, never reparented into it |
| `Converters/` | `BoolToVisibilityConverter`, `ColorToBrushConverter` |
| `app.manifest` | DPI awareness, compatibility, and execution level (`asInvoker`) |

### Services

| Service | Responsibility |
|---------|----------------|
| `WindowShepherdService` | TabDock's only capture backend. Positions/shows/hides an external HWND over the container's content area via `SetWindowPos`/`ShowWindow` — never reparents or restyles it. Release restores the capture-time `WINDOWPLACEMENT`. Also owns the `hidden-windows.json` crash-recovery journal (see `RescueOrphanedWindows`) and the single z-order pin implementation (`PairZOrderBehind`, shared with the container's foreground-pairing path) |
| `GroupManager` | Owns all groups; enforces flat, no-nesting rule; coordinates tab switching/reordering/release (including member-by-reference release via `ReleaseMember`). Also maintains the O(1) HWND→member index every WinEvent lookup goes through (`IsCapturedWindow`/`TryGetCapturedMember`) and the `MonitoringNeededChanged` signal that gates the hooks |
| `GuestLifecycleService` | The single consumer of `WinEventMonitor` events. Owns all WinEvent policy: destroy/hide teardown (guest-initiated-hide classification, empty-group container close), minimize restore, move/size re-glue routing, foreground z-order pairing, and the 250 ms name-change debounce. Interface is one `Attach(WinEventMonitor)` call; member resolution goes through `GroupManager.TryGetCapturedMember` |
| `PersistenceService` | Version-2 metadata persistence with v1 migration, future-version preservation, corrupt-vs-unreadable classification, valid-backup recovery only when safe, durable atomic writes, fail-safe overwrite blocking, and no accumulation/restoration of unmaterialized zero-tab shells |
| `WinEventMonitor` | Out-of-process `SetWinEventHook` wrapper for destroy/rename/minimize/foreground/move-size events on captured windows. Filters by direct member-HWND match — never by `GetAncestor`, which cannot see an already-destroyed window's ancestors. Hook installation is a bounded transaction with injected failure tests; capture admission is disabled while unhealthy and captured guests are released after retry exhaustion |
| `HotkeyService` | Registers global `Ctrl+Alt+G` hotkey (with `MOD_NOREPEAT`, so holding the key does not stack capture pickers) |
| `IconService` | Extracts executable icons for tab thumbnails, cached per (case-insensitive) exe path |
| `LoggingService` | Rotating file logger in `%APPDATA%\TabDock\logs\TabDock.log`. Callers only enqueue; a background thread batches queued lines through one persistent append handle. If storage is unavailable it keeps a bounded memory-only tail and reports the degraded capability |

`WindowCaptureService` (`SetParent`-based reparenting), `RenderHealthService` (`PrintWindow`-based black-frame detection), `DpiService` (DPI-forwarding to a reparented child), and `GuestActivationHelper` (synthetic activation messages) were deleted together in the Shepherd migration — all four existed solely to compensate for problems that `SetParent` reparenting caused and that Shepherd's never-reparent model doesn't have in the first place. They survive only as historical references in comments and docs.

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

Note this builds only the main app and the Spike — the ValidationDriver/GuineaPig projects are not in the solution and must be built by project path.

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

`docs/TESTING.md` is the consolidated testing playbook — read it before validating changes. The short version:

### Automated real-input test

`tests/ValidationDriver/TabDock.ValidationDriver` is a console harness that drives a fresh TabDock instance plus guinea-pig/real-app windows entirely through synthesized `SendInput` mouse/keyboard at UIA-read coordinates, then asserts on window state, pixels, and log output. It supports Debug/Release/RID/path selection and bounded named shards:

```powershell
dotnet run --project tests\ValidationDriver\TabDock.ValidationDriver\TabDock.ValidationDriver.csproj -- --configuration Release --yes <scenario|shard|all>
```

Use `--help` or `--list` for the authoritative options, scenarios, and shard
assignments. `all` launches each hermetic shard as a separate guarded child so
the 12-spawn/10-minute limits remain meaningful as coverage grows. Browser and
real-app shards are explicit-only. `TabDock.GuineaPig` is a tiny WinForms app
whose only job is to be captured/released/dragged while logging the window
messages it receives; its command-line switches (`--title`, `--color`,
`--pulse`, `--hide-on-close`, `--minimize-then-hide-on-close`,
`--self-close-after`, `--click-counter-button`, `--text-box`) let scenarios
exercise specific guest behaviors deterministically.

Since a Shepherd guest is never reparented, "is this guest captured/released" can no longer be read off `WS_CHILD`/`GetParent` (both are permanently unchanged) — scenarios instead compare the guest's `GetWindowRect` against the container's content-area marker (`IsDocked`/`IsReleasedAndShown`/`IsReleasedAndHidden` helpers in `Scenarios.cs`). Notable scenarios beyond the general capture/release/tab-switch coverage: `dragout-by-titlebar` (drag the guest's own native title bar past/under the pop-out threshold), `directclick-foreground-pairing` (click the guest directly, bypassing TabDock's own UI, and verify z-order re-pairing), `crashkill-rescue` (force-kill TabDock with a hidden tab captured, relaunch, verify the crash-recovery journal restores it), `realapp-multi-render` (real apps, `PrintWindow`-verified live rendering, byte-identical placement/style/exstyle/parent before vs. after capture+release — this replaced the old, Reparent-only `tests/CaptureReleaseTest` project, since a `PrintWindow` capture of a GPU-rendered guest reads its own back-buffer directly and isn't affected by whatever else is on top of it on screen, unlike a `BitBlt`-based screen-region capture), and `keyboardinput-*-altswitch` (the direct regression test for the originally-reported "keyboard input stops after switching to another app and back" bug). The `expand-e2e-coverage` change added eight pig-only scenarios to `all` — `container-minimize-retains-tabs` (H6), `hotkey-hold-single-picker`, `popout-inactive-keeps-active`, `double-capture-refused`, `persist-active-tab-index`, `restored-group-survives-member-reclose`, `selfminimize-timer-vs-teardown`, `launcher-empty-state-hint` — hardened `dragreorder`/`browser-dragreorder` with the H2 oscillation bound (flip-back-pair + churn-ceiling assertions on `Reordered tab` lines), extended `browser-tabswitch-hidesafety` with per-switch `PrintWindow` live-render checks (H4), and enforced the rule that no scenario may assert on log instrumentation absent from committed source.

The test requires interactive confirmation, spawns real applications, and kills them on completion or failure. Because it sends real input, do not touch the mouse or keyboard during a run, and do not run it unattended on a production machine. Known harness limitation: scenarios that must programmatically acquire foreground (`ForceForeground`) are flaky when the suite is driven from inside another foreground-holding interactive session (see `KNOWN_ISSUES.md`); treat such failures observed in that context as unverified until re-run standalone.

The two legacy PowerShell e2e scripts (`tests/e2e-capture-release.ps1`, `tests/e2e-stress-and-drag.ps1`) were removed: their Reparent-era assertions are invalid under the Shepherd never-reparent model (e.g. a vacuous `GetParent == 0` check), they hardcoded machine paths, and the ValidationDriver harness above supersedes them.

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
- Maximize/restore of the container with a docked guest

Use this checklist before considering a build ready for use.

### Survival spike

`Spike/TabDock.Spike` is an experimental console app that reparents a Command Prompt into a throwaway host and then force-kills the host to observe whether the child HWND survives. It is not part of normal CI; run it only when investigating OS-level reparenting behavior.

---

## Code style guidelines

- **Implicit usings are disabled.** Every source file must include explicit `using` directives.
- **Nullable reference types are enabled.** Mark nullable reference types with `?`.
- **XML doc comments** are expected on public types and non-trivial members, especially in `Services/` and `Models/`.
- **P/Invoke lives in one file only.** Add new native declarations to `NativeMethods.cs`; do not scatter them across the codebase.
- **File-scoped namespaces** are used throughout (`namespace TabDock.Services;`) — every source file in the main project declares its namespace this way, with no braces. Match that in new files. (This guide previously claimed the opposite; no file has ever used a block namespace.)
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

### Performance-sensitive paths

`docs/internal/perf-2026-07-25.md` records the `PERF25-NN` pass and the reasoning behind each change. Four of its results are invariants rather than local optimizations — breaking them reintroduces a cost the rest of the design assumes away:

1. **Resolve a WinEvent HWND through `GroupManager`'s index, never by scanning.** `IsCapturedWindow` runs for every destroy/hide/rename/minimize/foreground/move-size event on the entire desktop, and the `GuestLifecycleService` handlers run for every one that survives it. Use `TryGetCapturedMember`; do not add a `Groups.ToList()` + `FirstOrDefault` scan back into a handler. The index is maintained from `Group.Members`' `CollectionChanged`, so new capture or release paths need no bookkeeping of their own — do not "help" it by mutating the index directly.
2. **The hooks are gated on `GroupManager.IsMonitoringNeeded`.** They are installed on the first capture and removed after the last release (deferred one dispatcher turn). Anything that needs a WinEvent while nothing is captured would have to change that gate deliberately — and would need a reason, since every current handler acts only on captured members.
3. **Keep the hot log lines cheap.** `SHEPHERD[position]` is emitted per mouse tick during a container drag; do not add `DescribeWindow` (or any other P/Invoke-bearing helper) to it. Do keep emitting it per position — the `instant-tabswitch` ValidationDriver scenario waits for a fresh one after each switch.
4. **`LoggingService` holds its log file open.** Read it with `FileShare.ReadWrite` (as `tests/ValidationDriver/TabDockLog.cs` does); a bare `File.ReadAllText` will hit a sharing violation while TabDock runs.

### Window ownership and no-nesting rule

- `GroupManager.IsOwnWindow` checks process ID and a registered set of container HWNDs.
- `WindowShepherdService.Capture` rejects captures where the target PID equals the current process ID.
- Container windows register their own HWND and the `NativeHwndHost` HWND with `GroupManager`.

### Error handling

- Native errors are logged via `LoggingService` with `NativeMethods.FormatLastError()`.
- Render-health failures and window teardown are best-effort and must not crash the container.
- `LoggingService` itself is fail-safe; it catches and suppresses its own exceptions.

### Commit messages

One-line short imperative summary; no bare URLs and no `progress`/`WIP` placeholders as the summary.

### Known dead/inert code (do not clean up)

- `MainViewModel.SelectedGroup` — bound in `MainWindow.xaml` but never consumed.
- `GroupViewModel.CloseGroupCommand` / `CloseRequested` — no binding and no subscriber.
- `GroupViewModel.PickColorCommand` — deliberate inert placeholder (documented no-op; see `openspec/specs/group-color-picker`).
- `IconService.GetWindowIcon(IntPtr)` — no production callers; kept for the tests.
- Many `NativeMethods.cs` declarations exist solely for the ValidationDriver via its link-include of the file (annotated `Test-harness only` in the source) — an "unused code" cleanup pass will break the driver build.

### Spec-driven changes (OpenSpec)

The `openspec/` directory holds an OpenSpec workflow (`schema: spec-driven` in `openspec/config.yaml`). Current capability specs include `capture-picker-icons`, `container-activation-timers`, `crash-shutdown-coherence`, `diagnostics-logging`, `e2e-input-safety`, `e2e-scenario-coverage`, `elevation-guard`, `group-color-picker`, `hidden-window-journal`, `persistence-resilience`, `test-tooling-safety`, and `ui-ux-hardening`; the active `deep-audit-remediation-2026-08-13` change records the recovery, lifecycle, privacy, and qualification contracts until it is archived. When making a behavior-level change, check whether a spec or change proposal covers that area and keep it in sync. The OpenSpec workflow skills/commands are vendored across the agent-tool directories (`.claude/`, `.cursor/`, `.cline/`/`.clinerules/`, `.codex/`, `.kimi/`, `.kimi-code/`, `.kilocode/`, `.opencode/`) and are regenerated by the `openspec` CLI — do not hand-edit a single copy. The canonical copies live in `.claude/skills/` and `.claude/commands/opsx/`; every other copy is machine-generated output. After each `openspec` CLI regeneration, re-mirror them with `scripts\sync-agent-configs.ps1`, which applies each tool's filename/frontmatter convention and rewrites `/opsx:` references to `/opsx-` for the dash-form tools (`.cursor`, `.opencode`). Hand-edits to any non-`.claude` copy will be overwritten — edit `.claude` instead.

### Issue history documents

`KNOWN_ISSUES.md` and `docs/internal/investigation_findings.md` are running logs of past bug-hunt sessions (H/M/L-series issues, the Shepherd migration, harness findings). They are historical records, not a backlog — check them before "discovering" an already-documented issue or a known harness flake. `docs/internal/` also holds the original test plan (`TEST_PLAN.md`) and audit reports (`deep-audit-2026-07-17.md`, `audit-2026-07-25.md`).

---

## Security considerations

- **Elevation:** TabDock ships as a standard-user application. It detects elevated target processes and rejects the capture with a clear message rather than auto-elevating itself.
- **UIPI:** Capturing a window owned by a higher-integrity process is blocked by OS-level checks; a non-elevated TabDock cannot position or foreground such a window either.
- **Dependency surface:** The project uses only the Microsoft-maintained `System.Threading.AccessControl` package for the ACL-backed product-mutation mutex; no unrelated third-party NuGet packages are used. Local builds default to offline-friendly NuGet audit settings; CI Release qualification enables full vulnerability auditing and fails on audit warnings.
- **Persistence:** Only metadata is written to `%APPDATA%\TabDock\state.json`; the separate versioned crash journal contains identity and reversible presentation state needed for rescue. No application content or credentials are persisted. Future or unreadable state is preserved rather than overwritten.
- **Logs:** Diagnostic logs are written to `%APPDATA%\TabDock\logs\TabDock.log` and rotated at 1 MB. If the directory is unavailable, logging degrades to a bounded in-memory tail with a visible startup warning; capture still requires durable journal storage.

---

## Deployment

The intended distribution artifact is the self-contained single-file executable produced by `dotnet publish` (see Build commands above). There is no installer or MSIX package. The published executable has no external runtime dependency on the target machine.

---

## Known limitations

- **Guest self-maximize is a cosmetic gap.** If the user maximizes the docked guest itself (not via TabDock's own maximize), it fills the whole monitor, breaking the docked look — there is no reliable WinEvent signal that distinguishes a programmatic/self-maximize from the interactive move/size loop, so nothing corrects it. Not an input-correctness bug; out of scope for now.
- **Elevated windows** cannot be captured by a non-elevated TabDock instance.
- **Task Manager force-kill:** captured guest processes/windows survive a
  `taskkill /F` against TabDock (they were never reparented into its window
  tree). The versioned journal restores every identity-valid guest's recorded
  reversible presentation state on the next launch; a durable intentional-hide
  marker prevents tray-style guests from being resurrected. Recycled HWND/PID
  identities are rejected.

These limitations are documented in `README.md` and should not be treated as bugs to be fixed without changing the project's scope. GPU-rendered/Electron/DirectX apps showing black or frozen content were artifacts of the deleted Reparent backend (`SetParent` breaking DWM composition) and do not apply under Shepherd — a guest is never reparented and retains its own native rendering. Shepherd accepts a known DPI-unaware guest using physical outer-window geometry, although Windows may scale its content; physical mixed-DPI hardware remains an external qualification rather than an unconditional guarantee.

---

## Useful references

- `README.md` — user-facing documentation, manual test checklist, and known limitations.
- `docs/TESTING.md` — consolidated testing/validation playbook (harness reference, scenario list, repro techniques).
- `docs/internal/guarded-spawn-pattern.md` — mandatory guardrails for any process-spawning code.
- `docs/internal/perf-2026-07-25.md` — performance invariants and the reasoning behind them.
- `KNOWN_ISSUES.md` / `docs/internal/investigation_findings.md` — historical bug-hunt and migration records.
- `NativeMethods.cs` — authoritative reference for all native interop used by the project.
- `docs/ARCHITECTURE.md` — system map, WinEvent event→handler table, log-line index.
