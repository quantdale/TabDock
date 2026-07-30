## Why

The test tooling (Spike and ValidationDriver) and `LoggingService` have defects that range from pattern violations to a genuine risk of damaging windows the user owns:

- **Spike may reparent a console window it did not spawn** (`Spike/TabDock.Spike/Program.cs:439-470`): `FindCmdWindow` `EnumWindows`-matches *any* visible `ConsoleWindowClass` window or one whose title merely contains `"cmd.exe"`, with no PID comparison against the spawned process. It then `SetParent`s that foreign window into the throwaway host and `taskkill /F`s the host — potentially destroying a window the user owns. This is exactly the incident class `docs/internal/guarded-spawn-pattern.md` exists to prevent.
- **Spike spawns `taskkill` outside `SpawnGuarded`** (`Program.cs:156`): untracked by `SpawnedProcesses`/`KillAllTracked` and not counted against the spawn cap — a direct violation of the mandatory "everything routes through `SpawnGuarded`" rule.
- **Spike `--host`/`--checker` child modes parse untrusted command-line HWND/PID with no validation** (`Program.cs:207,315-316`): `long.Parse`/`int.Parse(args[...])` is `SetParent`'d (`:250`) and restyled with no `IsWindow`/PID/class check; malformed args throw `FormatException` mid-flow.
- **ValidationDriver hardcodes machine-specific absolute paths** (`tests/ValidationDriver/TabDock.ValidationDriver/Scenarios.cs:79-80`): `TabDockExe`/`PigExe` are string literals under `d:\Documents\tryPython\...`; the driver silently fails on any other machine. The browser paths (`:81-83`) probe standard Program Files locations but have no PATH fallback.
- **Two scenarios reuse a fixed Chrome profile dir** (`Scenarios.cs:2524,2649`), contradicting the documented fix at `FreshProfileDir` (`:626-629`) — force-killed Chrome shows a "Restore pages?" bubble that breaks window matching.
- **`LoggingService` `.err` fallback grows unboundedly** (`Services/LoggingService.cs:149,156`): on a persistent logging failure the `TabDock.log.err` file is appended with no rotation or cap.
- **Rotation-failure retry churn** (`:237-240`): a failed `File.Move` causes a close/delete/move/open cycle on every batch for the rest of the session.
- **`LoggingService.Dispose` is not re-entrant across threads** (`:251-263`): check-then-set on `_disposed` races; two concurrent callers both reach `CompleteAdding()` (second throws) — latent trap given crash paths can race `Dispose`.

## What Changes

- **Spike PID-ownership check** — `FindCmdWindow` must require `GetWindowThreadProcessId(candidate) == cmd.Id` before returning a window; continue the retry loop otherwise.
- **Spike spawn routing** — route the `taskkill` spawn through `SpawnGuarded` and bump `MaxTotalSpawns` accordingly.
- **Spike child-mode validation** — in `--host`/`--checker` modes, validate `IsWindow` + expected class/PID before `SetParent`, and fail cleanly on malformed args instead of `FormatException`.
- **ValidationDriver path resolution** — resolve exe paths relative to the driver's own assembly location and probe well-known browser install paths / PATH.
- **ValidationDriver profile dirs** — use `FreshProfileDir` in the two scenarios that still use the fixed `TabDockChromeProfile` temp dir.
- **LoggingService caps** — cap the `.err` file (keep last N KB or suppress repeated identical errors); back off rotation retries after a failure (retry at most every N batches); make `Dispose` re-entrant via `Interlocked.Exchange` on an int flag.
- **Adjacent one-liner** — `NativeMethods.GetMessage` is declared `bool` (native returns `-1` on error, which marshals to `true`); it has no call sites in the main project (the Spike declares its own correctly-typed version), so delete it or change it to `int`.

## Capabilities

### New Capabilities
- `test-tooling-safety`: Spike window-ownership validation, guarded-spawn compliance, child-mode argument validation, and ValidationDriver portability/profile freshness.
- `diagnostics-logging`: Bounded fallback logging, rotation backoff, and thread-safe logger disposal.

### Modified Capabilities
(none — test tooling and diagnostics only.)

## Impact

- **Code**: `Spike/TabDock.Spike/Program.cs`, `tests/ValidationDriver/TabDock.ValidationDriver/Scenarios.cs`, `Services/LoggingService.cs`, `NativeMethods.cs`.
- **No production behavior change** except `LoggingService` robustness (bounded `.err`, rotation backoff, re-entrant `Dispose`).
- **Explicitly out of scope**: restructuring Spike scenarios or adding new ValidationDriver coverage.
