# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

This repository already maintains `AGENTS.md` as the primary, actively-updated agent guide — read it in full before making changes. It covers architecture, code style, testing, security considerations, and known limitations in more detail than is repeated here. This file exists only to surface the commands and mental model you need most often.

## What TabDock is

A Windows desktop utility (C# / .NET 8 / WPF, `net8.0-windows`) that groups independent application windows (browser, terminal, editor, etc.) into a single container window with a browser-style tab strip. It does this by *positioning* guest windows over the container's content area, not by reparenting them (see Architecture below). No third-party NuGet packages — all native interop is P/Invoke declared in one place, `NativeMethods.cs`.

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

Run the real-input validation driver (drives a fresh TabDock plus guinea-pig windows entirely through synthesized `SendInput` mouse/keyboard at UIA-read coordinates, then asserts on window state, pixels (`PrintWindow` for GPU-rendered guests — see Architecture below; `BitBlt` elsewhere), and both TabDock's and the pigs' message logs; interactive, spawns real apps — pass `--yes` to skip confirmation):
```powershell
dotnet run --project tests\ValidationDriver\TabDock.ValidationDriver\TabDock.ValidationDriver.csproj -- [--yes] [--cycles N] [--guest pig|wt|chrome-nogpu|chrome-gpu] <scenario|all>
```
`TabDock.GuineaPig` is the disposable target app it spawns. Neither `ValidationDriver` project is in `TabDock.sln` — build/run them by project path.

There is no automated unit test suite; `README.md` has a manual test checklist to run before considering a build ready.

When debugging runtime behavior, the app writes a rotating log to `%APPDATA%\TabDock\logs\TabDock.log` (rotated at 1 MB to `TabDock.log.old`).

## Architecture: the pieces that require reading multiple files to understand

**Shepherd is TabDock's only capture backend** (`Services/WindowShepherdService.cs`; see `docs/internal/deep-audit-2026-07-17.md` §6 for the design rationale). A captured guest is *never* reparented or restyled — it stays a real, independent top-level window for its entire captured lifetime. When it's the active tab, `SetWindowPos` positions it exactly over the container's content-area marker and brings it to `HWND_TOP`; the container is then pinned immediately behind it in z-order (`SetWindowPos` with `hwndInsertAfter` = the guest — note the direction: `SetWindowPos(hWnd, hWndInsertAfter, ...)` places `hWnd` *after*, i.e. **behind**, `hWndInsertAfter`, so the guest must be the one passed as `hWndInsertAfter` when pinning the container, not the other way around — this exact reversal was a real, shipped bug once and is easy to reintroduce). When it's an inactive tab it's hidden with `ShowWindow(SW_HIDE)`. Release just restores the capture-time placement (`WINDOWPLACEMENT`) — there is no style/parent surgery to get wrong because none was ever done. `ContainerWindow` tracks the active shepherded window separately (`_shepherdActiveWindow`) and drives `PositionAndShow`/`Hide`/`BringToFront`.

This eliminates an entire bug class by construction: because the guest's input queue is never joined to TabDock's (no `AttachThreadInput`, no `SetParent`), Windows manages its keyboard focus and activation exactly as if TabDock didn't exist. There is no attach/detach state machine left to race and no synthetic `WM_ACTIVATE` to get wrong. A prior "Reparent" backend (`WindowCaptureService`, `SetParent`-based) was the root cause of a recurring keyboard-input-lost-after-app-switch bug and has been deleted entirely, along with `DpiService`, `GuestActivationHelper`, and `RenderHealthService` (DPI-forwarding, activation-message-forwarding, and black-frame-detection all existed solely to compensate for `SetParent` breaking things — none of that is needed when nothing is ever reparented).

Two follow-on behaviors exist specifically because the guest is a real, independent top-level window the whole time:

- **Z-order pairing on external foreground changes.** If the user alt-tabs to the guest directly (via Windows' own switcher) or clicks it without touching TabDock's tab strip, `WinEventMonitor`'s `WindowForegroundChanged` (wired in `App.xaml.cs`) calls `ContainerWindow.PairZOrderBehindGuest` to re-pin the container immediately behind it, keeping the docked illusion consistent — this is purely cosmetic z-order bookkeeping, never a focus/input fix (input already routes to the guest correctly regardless).
- **Drag-out by the guest's own real title bar.** The guest keeps its native title bar while docked (a deliberate v1 cosmetic tradeoff — reversibly stripping `WS_CAPTION` was considered and rejected as reintroducing the exact style-mutation risk this backend exists to avoid). `ContainerWindow.NoteGuestMoveSize`, driven by `WindowMoveSizeEnded`, treats a post-drag rect more than `DragOutThresholdPx` (40px) from the docked position as an intentional pop-out and releases the tab (restoring the pre-capture placement, the same as every other release path — the guest does *not* stay wherever it was dropped); a smaller drag snaps back exactly.

`GroupManager` owns the set of `Group`s and coordinates which capture belongs to which container, tab order, and active-tab switching. `WinEventMonitor` runs an out-of-process `SetWinEventHook` to detect when a captured window is destroyed, renamed, minimized, or brought to foreground externally, and feeds those events back into `GroupManager`/`ContainerWindow` so tab state stays in sync with reality outside TabDock's control. Its filter is a direct captured-member HWND match (`GroupManager.IsCapturedWindow`) — do not reintroduce `GetAncestor(GA_ROOT)`/own-window filtering there: a destroyed window has no ancestors, so that would silently drop the exact events the monitor exists to observe.

**No-nesting invariant, enforced in two places at once.** `GroupManager.IsOwnWindow` checks PID plus a registered set of container HWNDs; `WindowShepherdService.Capture` separately rejects captures where the target PID equals the current process. Container windows register both their own HWND and their `NativeHwndHost`'s HWND with `GroupManager` at creation. Any change to capture logic must preserve both checks — losing either reopens the nested-group case.

**Emergency release is a cross-cutting concern, not a single function.** Captured windows must return to standalone on normal exit, unhandled exceptions, and dispatcher crashes. This logic lives in `App.xaml.cs` alongside service lifetime management, not in `WindowShepherdService` itself — the two are separate because release-on-crash needs to run even if the object graph is partially torn down.

**Persistence is layout intent, not window state.** `PersistenceService` writes only group name, accent color, tab order, and executable paths to `%APPDATA%\TabDock\state.json` (via source-generated `TabDockJsonContext`), because HWNDs don't survive reboots. On startup, groups are restored as empty shells for the user to re-populate — there is no attempt to re-attach live windows.

**A force-killed TabDock no longer destroys captured guests — but a hidden one needs rescuing.** Because a guest is never reparented, killing TabDock (crash, Task Manager) leaves every captured guest process and window intact — a strict improvement over the old Reparent model (see the watchdog note below). The one gap this opens: a guest that was *hidden* (an inactive tab) at the moment of the kill has no way to reappear on its own. `WindowShepherdService` journals every hide to `%APPDATA%\TabDock\hidden-windows.json` (hwnd, pid, exe path), clearing the entry on show/release; `RescueOrphanedWindows`, called once at startup before groups are restored, validates each entry (HWND still valid, owning PID and exe path unchanged) and shows it again, then unconditionally clears the journal — a same-session recovery aid only, matching the "layout intent only" persistence philosophy (HWNDs don't survive reboots regardless).

**Process spawning is gated everywhere, not just in the picker.** Any code calling `Process.Start` — the capture picker's "Group these" action, future launch/wizard logic, watchdog-style helpers, test harnesses — must follow `docs/internal/guarded-spawn-pattern.md` (hard spawn cap, no bare retry loops, named mutex for single-instance tools, tracked-process cleanup, hard timeout, flushed logging, manual confirmation for destructive one-offs). This was made mandatory after a runaway self-recursion incident in `Spike/TabDock.Spike`; do not bypass it for "just a quick test."

**A watchdog *process* was deliberately not built** (distinct from the in-process journal above). The survival spike (`Spike/TabDock.Spike`) proved that a `WS_CHILD` window reparented into a host does not survive `taskkill /F` on the host — this was true of the old Reparent backend and is exactly the failure mode Shepherd's never-reparent design sidesteps. Don't propose a separate external watchdog process without re-litigating this finding (see `docs/internal/guarded-spawn-pattern.md`).

## Solution layout

`TabDock.sln` has two projects: `TabDock.csproj` (main app) and `Spike/TabDock.Spike` (experimental, not part of normal CI). The main csproj explicitly excludes `Spike/**`, `tests/**`, and `docs/**` from its default item globs so SDK-style wildcard includes don't pull spike/test source into the app build. (`tests/CaptureReleaseTest` — a Reparent-only e2e harness whose entire premise, verifying parent/style/exstyle/placement were correctly *restored*, evaporates when nothing is ever mutated in the first place — has been deleted; its unique value, real apps plus `PrintWindow`-based render verification, lives on as the `realapp-multi-render` ValidationDriver scenario.)

Two more projects live under `tests/ValidationDriver/` (`TabDock.ValidationDriver` and `TabDock.GuineaPig`) but are **not** in `TabDock.sln`; build and run them via their project paths. `ValidationDriver` link-includes the app's `NativeMethods.cs` rather than referencing the app, so it stays a standalone console tool.
