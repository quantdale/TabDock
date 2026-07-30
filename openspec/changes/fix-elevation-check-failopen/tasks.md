# Tasks

## 1. Fail-closed capture guard

- [ ] 1.1 Split the compound `IsProcessElevated(pid, out targetElevated) && targetElevated` condition in `WindowShepherdService.Capture` so a failed check (return `false`) is handled distinctly from a successful not-elevated result
- [ ] 1.2 Reject with a clear message when the elevation check is indeterminate and TabDock is not elevated
- [ ] 1.3 Log the underlying native error with `FormatLastError()` when the check fails (capture the last error inside `IsProcessElevated` before any intervening call can clobber it)

## 2. Positioning API error visibility

- [ ] 2.1 Check `SetWindowPos`/`ShowWindow`/`SetForegroundWindow` returns in `PositionAndShow`/`Hide`/`Release`; log `FormatLastError()` at most once per window (no per-drag-tick cost)
- [ ] 2.2 Add `SetLastError = true` to `ExtractIconEx`; log when it returns `0xFFFFFFFF` in `IconService`

## 3. Validation

- [ ] 3.1 `dotnet build TabDock.sln` clean
- [ ] 3.2 ValidationDriver: capture scenarios still pass for standard-user targets; elevated-target rejection verified manually
