## Why

`NativeMethods.IsProcessElevated` (`NativeMethods.cs:747-788`) returns a plain `bool` where "checked, not elevated" and "check failed" are indistinguishable. If `OpenProcess`, `OpenProcessToken` (deniable by a hardened token DACL), or `GetTokenInformation` fails, the guard in `WindowShepherdService.Capture` (`Services/WindowShepherdService.cs:86`) is skipped entirely and capture of a possibly-elevated window proceeds — contradicting the documented behavior that elevated windows are "detected and rejected with a clear message". Every subsequent UIPI-blocked `SetWindowPos`/`ShowWindow` then fails silently (return values never checked), leaving the guest floating and unpositioned with only normal `SHEPHERD[position]` log lines.

## What Changes

- **Tri-state elevation API** — replace `IsProcessElevated(uint pid)` with `TryIsProcessElevated(uint pid, out bool elevated)` (or an `ElevationCheckResult` enum) in `NativeMethods.cs`, distinguishing check-failure from not-elevated.
- **Fail-closed capture guard** — in `Capture`, treat an indeterminate result as a block unless the current process itself is elevated; log the underlying native error with `FormatLastError()` so the bypass is visible.
- **Positioning API error visibility** — check `SetWindowPos`/`ShowWindow`/`SetForegroundWindow` return values in `PositionAndShow`/`Hide`/`Release` and log `FormatLastError()` at most once per window (keeps the hot drag path cheap per the PERF25-3 invariant — no per-tick error queries).
- **Adjacent one-liner** — add `SetLastError = true` to `ExtractIconEx` and log in `IconService` when it returns `0xFFFFFFFF`, so "no icons in file" vs "file unreadable" is diagnosable.

## Capabilities

### New Capabilities
- `elevation-guard`: Tri-state elevation determination, fail-closed capture on indeterminate elevation, and once-per-window positioning-failure logging.

### Modified Capabilities
(none — no existing spec covers the elevation guard; behavior now matches the already-documented intent.)

## Impact

- **Code**: `NativeMethods.cs`, `Services/WindowShepherdService.cs`, `Services/IconService.cs`.
- **Behavior change**: captures that previously proceeded on an indeterminate-elevation target are now refused with a clear message. This is the documented intent, so it is a bug fix, not a regression.
- **No API/dependency changes.**
