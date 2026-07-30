## Why

`NativeMethods.IsProcessElevated(uint pid, out bool elevated)` (`NativeMethods.cs:747-788`) already reports check success/failure in its return value separately from the elevation outcome in the `out` parameter, but the guard in `WindowShepherdService.Capture` (`Services/WindowShepherdService.cs:86`) collapses the two into one compound condition — `if (IsProcessElevated(pid, out bool targetElevated) && targetElevated)` — so "checked, not elevated" and "check failed" are indistinguishable at the call site. If `OpenProcess`, `OpenProcessToken` (deniable by a hardened token DACL), or `GetTokenInformation` fails, the guard is skipped entirely and capture of a possibly-elevated window proceeds — contradicting the documented behavior that elevated windows are "detected and rejected with a clear message". Every subsequent UIPI-blocked `SetWindowPos`/`ShowWindow` then fails silently (return values never checked), leaving the guest floating and unpositioned with only normal `SHEPHERD[position]` log lines.

## What Changes

- **Fail-closed capture guard** — split the compound `&&` condition in `Capture` so a failed check (return `false`) is handled distinctly from a successful not-elevated result: treat an indeterminate result as a block unless the current process itself is elevated, and log the underlying native error with `FormatLastError()` so the bypass is visible. No API signature change is needed — `IsProcessElevated` already returns the two outcomes separately.
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
