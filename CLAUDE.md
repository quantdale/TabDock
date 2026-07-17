# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

This repository already maintains `AGENTS.md` as the primary, actively-updated agent guide — read it in full before making changes. It covers architecture, code style, testing, security considerations, and known limitations in more detail than is repeated here. This file exists only to surface the commands and mental model you need most often.

## What TabDock is

A Windows desktop utility (C# / .NET 8 / WPF, `net8.0-windows`) that reparents independent application windows (browser, terminal, editor, etc.) into a single container window with a browser-style tab strip. No third-party NuGet packages — all native interop is P/Invoke declared in one place, `NativeMethods.cs`.

Style facts that bite when writing new files: `ImplicitUsings` is **disabled** (every file needs explicit `using` directives), nullable reference types are enabled, and the project uses classic block namespaces, not file-scoped ones. New P/Invoke goes in `NativeMethods.cs` only.

## Commands

Build the main app:
```powershell
dotnet build TabDock.csproj
```

Build the whole solution (main app + test harness + spike):
```powershell
dotnet build TabDock.sln
```

Run the dev build:
```powershell
.\bin\Debug\net8.0-windows\win-x64\TabDock.exe
```

Publish the self-contained single-file release (Native AOT is intentionally not used — WPF needs COM activation/reflection emit that's incompatible with trimming/AOT):
```powershell
dotnet publish TabDock.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Run the capture/release end-to-end test harness (spawns real apps — Paint, Windows Terminal, Chrome, Cursor — captures, verifies live rendering, releases, and diffs HWND state; requires interactive confirmation, not for unattended/CI use):
```powershell
dotnet run --project tests\CaptureReleaseTest\TabDock.CaptureReleaseTest\TabDock.CaptureReleaseTest.csproj
```

Run the real-input validation driver (drives a fresh TabDock plus guinea-pig windows entirely through synthesized `SendInput` mouse/keyboard at UIA-read coordinates, then asserts on window state, `BitBlt` pixels, and both TabDock's and the pigs' message logs; interactive, spawns real apps — pass `--yes` to skip confirmation):
```powershell
dotnet run --project tests\ValidationDriver\TabDock.ValidationDriver\TabDock.ValidationDriver.csproj -- [--yes] [--cycles N] [--guest pig|wt|chrome-nogpu|chrome-gpu] <scenario|all>
```
`TabDock.GuineaPig` is the disposable target app it spawns. Neither `ValidationDriver` project is in `TabDock.sln` — build/run them by project path.

There is no automated unit test suite; `README.md` has a manual test checklist to run before considering a build ready.

When debugging runtime behavior, the app writes a rotating log to `%APPDATA%\TabDock\logs\TabDock.log` (rotated at 1 MB to `TabDock.log.old`).

## Architecture: the pieces that require reading multiple files to understand

**Two capture backends, chosen per group.** `Group.Mode` (`GroupCaptureMode`, `Models/Group.cs`) selects between `Reparent` (default) and `Shepherd`, and is fixed for a group's lifetime — flipping it under a live capture would desync which backend owns a window. `GroupManager` holds both services and routes release to whichever backend captured the window.

- **Reparent** (`WindowCaptureService`) is the original model: it reparents an external HWND into a `NativeHwndHost` (an `HwndHost` subclass in `Infrastructure/`) owned by a `ContainerWindow`, mutating parent/style/exstyle and restoring the exact prior parent/style/exstyle/bounds/placement on release. Everything downstream (DPI forwarding, drift watchdog, render-health) exists to compensate for `SetParent` breaking input/DPI/rendering.
- **Shepherd** (`WindowShepherdService`) is the experimental backend from `docs/internal/deep-audit-2026-07-17.md` §6: it *never* reparents or restyles the guest. The guest stays a top-level window, positioned over the container's content area and z-ordered just above it (`SetWindowPos`), hidden with `SW_HIDE` when it's not the active tab. Release just restores the capture-time placement — no style/parent surgery to get wrong. `ContainerWindow` tracks the shepherded active window separately (`_shepherdActiveWindow`) and drives `PositionAndShow`/`Hide`/`BringToFront`. The deep audit argues the reparenting model is the root cause of the recurring DPI/input/render bugs and recommends migrating to this coordinate-don't-adopt model; treat Shepherd as the strategic direction, not a dead experiment.

`GroupManager` owns the set of `Group`s and coordinates which capture belongs to which container, tab order, and active-tab switching. `WinEventMonitor` runs an out-of-process `SetWinEventHook` to detect when a captured window is destroyed, renamed, minimized, or brought to foreground externally, and feeds those events back into `GroupManager`/`ContainerWindow` so tab state stays in sync with reality outside TabDock's control. Its filter is a direct captured-member HWND match (`GroupManager.IsCapturedWindow`) — do not reintroduce `GetAncestor(GA_ROOT)`/own-window filtering there: a captured window's root *is* the TabDock container, and a destroyed window has no ancestors, so both would silently drop the exact events the monitor exists to observe.

**No-nesting invariant, enforced in two places at once.** `GroupManager.IsOwnWindow` checks PID plus a registered set of container HWNDs; `WindowCaptureService` separately rejects captures where the target PID equals the current process. Container windows register both their own HWND and their `NativeHwndHost`'s HWND with `GroupManager` at creation. Any change to capture logic must preserve both checks — losing either reopens the nested-group case.

**Emergency release is a cross-cutting concern, not a single function.** Captured windows must return to standalone on normal exit, unhandled exceptions, and dispatcher crashes. This logic lives in `App.xaml.cs` alongside service lifetime management, not in `WindowCaptureService` itself — the two are separate because release-on-crash needs to run even if the object graph is partially torn down.

**Persistence is layout intent, not window state.** `PersistenceService` writes only group name, accent color, tab order, and executable paths to `%APPDATA%\TabDock\state.json` (via source-generated `TabDockJsonContext`), because HWNDs don't survive reboots. On startup, groups are restored as empty shells for the user to re-populate — there is no attempt to re-attach live windows.

**Render health is a recovery loop, not a one-shot check.** `RenderHealthService` uses `PrintWindow` to detect black/frozen GPU-rendered tabs (common with Chrome/Electron/DirectX apps reparented into a foreign parent) and triggers automatic release back to standalone rather than leaving a dead-looking tab in the container.

**Process spawning is gated everywhere, not just in the picker.** Any code calling `Process.Start` — the capture picker's "Group these" action, future launch/wizard logic, watchdog-style helpers, test harnesses — must follow `docs/internal/guarded-spawn-pattern.md` (hard spawn cap, no bare retry loops, named mutex for single-instance tools, tracked-process cleanup, hard timeout, flushed logging, manual confirmation for destructive one-offs). This was made mandatory after a runaway self-recursion incident in `Spike/TabDock.Spike`; do not bypass it for "just a quick test."

**A watchdog process was deliberately not built.** The survival spike (`Spike/TabDock.Spike`) proved that a `WS_CHILD` window reparented into a host does not survive `taskkill /F` on the host — Windows destroys the child HWND as part of tearing down the dead parent's window tree. There is no surviving HWND for an external process to rescue. Don't propose re-adding watchdog-based crash recovery without re-litigating this finding (see `docs/internal/guarded-spawn-pattern.md`).

## Solution layout

`TabDock.sln` has three projects: `TabDock.csproj` (main app), `tests/CaptureReleaseTest/TabDock.CaptureReleaseTest` (e2e harness), and `Spike/TabDock.Spike` (experimental, not part of normal CI). The main csproj explicitly excludes `Spike/**`, `tests/**`, and `docs/**` from its default item globs so SDK-style wildcard includes don't pull spike/test source into the app build.

Two more projects live under `tests/ValidationDriver/` (`TabDock.ValidationDriver` and `TabDock.GuineaPig`) but are **not** in `TabDock.sln`; build and run them via their project paths. `ValidationDriver` link-includes the app's `NativeMethods.cs` rather than referencing the app, so it stays a standalone console tool.
