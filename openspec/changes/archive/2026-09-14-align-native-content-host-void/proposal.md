## Why

The 2026-09-12 frontend overhaul tokenized the container content void to
`TdContentVoidBrush` (`#07090D`) and contract-tested the WPF `ContentBorder`
against that token. The actually visible void is not that WPF brush: the native
`TabDockContentHost` child HWND always paints above WPF siblings (airspace).
`NativeHwndHost` still registers `CreateSolidBrush(0x001E1E1E)` and still
comments that it matches WPF `#1E1E1E`. On an empty workspace the centered
empty-state popup therefore sits on a stale `#1E1E1E` field; the same mismatch
flashes during tab-switch and layout gaps. The DWM chrome helper already has a
token lockstep test; the content-host brush was missed.

## What Changes

- Register the native content-host class background from the same RGB as
  `TdContentVoidBrush` (`#07090D` → COLORREF `0x000D0907`), keeping the
  `TabDockContentHost` class name unchanged.
- Extend `FrontendDesignContractTests` with the same source-lockstep pattern
  already used for `WindowChromeTheme` versus `App.xaml`.
- Document the airspace/token lockstep in `docs/FRONTEND.md` so a later palette
  change cannot retokenize WPF without the native host.

## Capabilities

### New Capabilities

- None. This is a correction to an existing presentation contract, not a new
  product surface.

### Modified Capabilities

- `ui-ux-hardening`: require the native content marker to paint the current
  `TdContentVoidBrush` color so airspace cannot expose a stale void, and
  require a structural test that keeps that native COLORREF in lockstep with
  the shared token.

## Impact

- `Infrastructure/NativeHwndHost.cs` (class background brush and comment).
- `tests/UnitTests/FrontendDesignContractTests.cs` (lockstep assertion).
- `docs/FRONTEND.md` (airspace rule).
- Runtime appearance of the empty-workspace field and any uncovered content
  rect. No Shepherd, capture, persistence, recovery, or automation-ID change.
  Not **BREAKING**: the host class name, HWND role, and driver discovery
  contract stay the same.
