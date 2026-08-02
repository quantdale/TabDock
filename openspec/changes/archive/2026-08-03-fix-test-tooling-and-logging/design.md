# Design — fix-test-tooling-and-logging

## Context

The change hardens test tooling and diagnostics that had pattern violations or could damage windows the user owns:

- The Spike (`Spike/TabDock.Spike/Program.cs`) originally reparented **any** visible `ConsoleWindowClass` window (or one whose title contained `cmd.exe`) via `FindCmdWindow`, with no PID comparison against the `cmd.exe` process it had just spawned, then `taskkill /F`'d the throwaway host — a direct risk of destroying a console the user owned. It also spawned `taskkill` outside `SpawnGuarded`, so it escaped the spawn cap and `KillAllTracked`. Its `--host`/`--checker` child modes `long.Parse`'d/`int.Parse`'d command-line HWND/PID with no `IsWindow`/class/process validation before `SetParent`.
- The ValidationDriver (`tests/ValidationDriver/TabDock.ValidationDriver/Scenarios.cs`) hardcoded machine-specific absolute paths for `TabDockExe`/`PigExe` and reused a fixed Chrome profile dir, which could surface a "Restore pages?" bubble after a force-killed browser and break window matching.
- `LoggingService` (`Services/LoggingService.cs`) had an unbounded `.err` fallback file, rotation-failure retry churn, and a non-re-entrant `Dispose` that could throw under concurrent callers.
- `NativeMethods.GetMessage` was declared `bool` (native returns `-1` on error, which marshals to `true`) with no call sites in the main project.

Constraints: no third-party packages; the guarded-spawn pattern (`docs/internal/guarded-spawn-pattern.md`) is mandatory for every `Process.Start`; all code must keep the zero-warning build and the perf invariants in `docs/internal/perf-2026-07-25.md`.

## Goals / Non-Goals

**Goals:**

- The Spike may only ever reparent, restyle, hide, or kill a console window it spawned itself.
- Every Spike spawn — including the internal `taskkill` — routes through `SpawnGuarded` and is torn down by `KillAllTracked` on every exit path.
- `--host`/`--checker` validate their HWND/PID arguments and fail cleanly on malformed or foreign input, performing no reparenting on failure.
- The ValidationDriver resolves its own exe paths portably and uses fresh per-run browser profiles.
- `LoggingService` stays bounded, backs off rotation retries, and survives concurrent `Dispose`.

**Non-Goals:**

- Restructuring Spike scenarios or adding new ValidationDriver coverage (that is `expand-e2e-coverage`'s job).
- Any production behavior change beyond `LoggingService` robustness.

## Decisions

### D1: Spike ownership is established by process-tree verification, not class/title matching

`FindCmdWindow` is replaced by a two-stage discovery. First, `SnapshotVisibleConsoleWindows` records every visible `ConsoleWindowClass` HWND *before* the orchestrator spawns its `cmd.exe`; second, `FindNewConsoleWindow` picks a visible `ConsoleWindowClass` HWND that is **not** in that snapshot and whose owning conhost either *is* the spawned `cmd.exe` or has it as a direct child (`FindChildProcessId`). Rationale: the diff confines the Spike to its own console, and the cmd-child check rejects a console created by someone else mid-run; HWND reuse is impossible for a window visible at snapshot time. The `--host` child mode independently re-verifies the passed HWND with `IsWindow` + `ConsoleWindowClass` class + PID (accepting a conhost whose parent is the expected cmd), so even a bad cross-process value is refused before any `SetParent`.

### D2: Every spawn, including `taskkill`, goes through `SpawnGuarded`

`taskkill /F /PID <host>` is spawned via `SpawnGuarded`, which counts it against `MaxTotalSpawns = 4` (1 conhost + 1 host + 1 checker + 1 taskkill) and adds it to `SpawnedProcesses` so `KillAllTracked` kills it on every exit path. The console owner (which can differ from the returned `Process` after conhost handoff) is added to the tracked set via `TrackConsoleOwner`, so teardown covers the whole console.

### D3: Child modes validate before touching anything

`--host` and `--checker` parse with `TryParse` and range checks (nonzero HWND, positive PID) and exit with a clear message on malformed arguments instead of an unhandled `FormatException`. `--host` additionally requires `IsWindow(childHwnd)` true and the class/PID ownership checks from D1 before registering the window class or calling `SetParent`.

### D4: Portable path resolution walks up to the repo root

`RepoRoot` is located by walking up from `AppContext.BaseDirectory` until a directory containing `TabDock.sln` is found; `TabDockExe`/`PigExe` are built from it. `FindExe` probes the well-known Program Files / LocalAppData install paths first, then falls back to a PATH lookup, then to the bare executable name so `Process.Start`'s own search produces a clear error. This replaces the hardcoded `d:\Documents\tryPython\...` literals.

### D5: Browser scenarios always use `FreshProfileDir`

The two scenarios that used the fixed `TabDockChromeProfile` temp dir now call `FreshProfileDir(...)` for a per-run unique `--user-data-dir`, matching the documented pattern at `FreshProfileDir` (`Scenarios.cs:626`), so a force-killed run's "Restore pages?" bubble can never break window matching.

### D6: LoggingService stays bounded and safe to dispose

- The `.err` fallback file is capped at `MaxErrFileSize` (64 KB): it is deleted before appending once it exceeds the cap, and consecutive identical error lines are suppressed via `_lastErrLine` — a persistent failure can never fill the disk.
- After a failed rotation, `_batchesUntilRotationRetry` backs the next attempt off to every `RotationRetryEveryBatches` (20) batches instead of a close/delete/move/open cycle per batch for the rest of the session; the write handle is reopened either way so logging never stops.
- `Dispose` uses `Interlocked.Exchange(ref _disposeStarted, 1)` so exactly one concurrent caller performs `CompleteAdding()`/`Join`; the rest return immediately without throwing. `_queue.Dispose()` only runs if the writer thread actually joined within the 2 s budget.

### D7: `NativeMethods.GetMessage` is deleted

It has no call sites in the main project and is declared `bool` despite the native `int` return (which is `-1` on error, marshaling to `true`). The Spike declares its own correctly-typed `int`-returning version, so the main-project declaration is dead, wrong-typed code and is removed.

## Risks / Trade-offs

- [The console-ownership diff misses a console that appears after the snapshot for a non-cmd reason] → the cmd-child check (`FindChildProcessId`) rejects any new console that is not running the orchestrator's `cmd.exe`; worst case is a timeout with a clear error, never touching the wrong window.
- [Rotation retry backoff delays the next rotation attempt after a transient failure] → bounded cadence (20 batches) still re-opens the handle every batch; only the move is deferred, and a stuck rotation costs one open/close cycle per 20 batches instead of per batch.
- [A capped `.err` file loses old diagnostics] → the cap (64 KB) only engages under a persistent logging failure, where the volume is the problem; the primary log remains the diagnostic source of truth.
- [Portable path resolution changes where the driver looks for builds] → the repo-root walk keeps the same `bin\Debug\...` layout; only the absolute machine-specific prefix is removed.

## Migration Plan

No data migration. The changes are confined to test tooling and `LoggingService`; the main application behavior is unchanged except for the logging robustness fixes. Rollback is a revert of the affected files. Validation: `dotnet build TabDock.sln` plus the Spike and ValidationDriver projects clean (task 6.1), a Spike run with a pre-existing console open confirms it is untouched (task 6.2), and a ValidationDriver `all` run confirms the harness still passes after the path/profile changes (task 6.3).

## Open Questions

None — the design and implementation are complete and validated by the task checklist.
