# Tasks

## 1. Fail-closed capture guard

- [x] 1.1 Split the compound `IsProcessElevated(pid, out targetElevated) && targetElevated` condition in `WindowShepherdService.Capture` so a failed check (return `false`) is handled distinctly from a successful not-elevated result
- [x] 1.2 Reject with a clear message when the elevation check is indeterminate and TabDock is not elevated
- [x] 1.3 Log the underlying native error with `FormatLastError()` when the check fails (capture the last error inside `IsProcessElevated` before any intervening call can clobber it)

## 2. Positioning API error visibility

- [x] 2.1 Check `SetWindowPos`/`ShowWindow`/`SetForegroundWindow` returns in `PositionAndShow`/`Hide`/`Release`; log `FormatLastError()` at most once per window (no per-drag-tick cost)
- [x] 2.2 Add `SetLastError = true` to `ExtractIconEx`; log when it returns `0xFFFFFFFF` in `IconService`

## 3. Validation

- [x] 3.1 `dotnet build TabDock.sln` clean
- [x] 3.2 ValidationDriver: capture scenarios still pass for standard-user targets; elevated-target rejection verified manually
  - `closewin`, `tabswitch-hidesafety` PASS in-session; `popout` PASS on a standalone rerun (the two in-session failures were the documented `ForceForeground` harness flake from `KNOWN_ISSUES.md` — foreground held by the driving CLI session — not a regression).
  - Elevated-target rejection verified end-to-end: an elevated Registry Editor (PID 91964, UAC-consented) was passed directly to `WindowShepherdService.Capture` from a throwaway harness referencing the built `TabDock.dll`; `IsProcessElevated` returned `ok=True elevated=True` and capture was REJECTED with "Cannot capture an elevated window. Run TabDock as administrator or choose a non-elevated window." (The harness's own log line was lost to `LoggingService`'s background-thread batch on immediate process exit; the rejection itself and its user-facing message were observed directly.)
