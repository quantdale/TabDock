# Tasks

## 1. Tri-state elevation API

- [ ] 1.1 Replace `NativeMethods.IsProcessElevated` with `TryIsProcessElevated(uint pid, out bool elevated)` (or an enum result), distinguishing check-failure from not-elevated
- [ ] 1.2 Update all call sites (currently `WindowShepherdService.Capture`)

## 2. Fail-closed capture guard

- [ ] 2.1 In `Capture`, reject with a clear message when the elevation check is indeterminate and TabDock is not elevated
- [ ] 2.2 Log the underlying native error with `FormatLastError()` when the check fails

## 3. Positioning API error visibility

- [ ] 3.1 Check `SetWindowPos`/`ShowWindow`/`SetForegroundWindow` returns in `PositionAndShow`/`Hide`/`Release`; log `FormatLastError()` at most once per window (no per-drag-tick cost)
- [ ] 3.2 Add `SetLastError = true` to `ExtractIconEx`; log when it returns `0xFFFFFFFF` in `IconService`

## 4. Validation

- [ ] 4.1 `dotnet build TabDock.sln` clean
- [ ] 4.2 ValidationDriver: capture scenarios still pass for standard-user targets; elevated-target rejection verified manually
