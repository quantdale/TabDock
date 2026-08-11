## Why

A user manually attempting to capture a DPI-unaware window at non-100% display
scaling was blocked by the fail-closed DPI guard added during the whole-codebase
audit:

> This window is not DPI-aware and can only be captured reliably at 100%
> display scaling.

That blanket refusal rested on the premise that an unaware guest "would be
stretched and misplaced no matter what rect we hand it." Native evidence proved
that premise false for OUTER-rect positioning: a PerMonitorV2 caller's
`SetWindowPos` with a physical pane rectangle is honored exactly on any top-level
window, including a DPI-unaware one (`GetWindowRect` round-trips the physical
rect; the unaware window is DWM-bitmap-stretched — blurry — exactly as it looks
standing alone, which is not a TabDock geometry defect). The refusal also used
`GetDpiForSystem()` (the PRIMARY monitor) to decide scaling, misclassifying
targets on differently-scaled secondary monitors.

## What Changes

- A KNOWN DPI-unaware guest is captured normally (no blanket refusal). The
  physical-pixel Shepherd contract already glues it exactly; only its content is
  DWM-scaled as it is standalone.
- The scale source becomes the target window's own monitor's effective DPI
  (`GetDpiForMonitor(hmon, MDT_EFFECTIVE_DPI)` via shcore from the PerMonitorV2
  thread), not `GetDpiForSystem()`. This is correct on mixed-DPI multi-monitor
  setups and never conflates a monitor's scale with the primary's.
- Refusal is strictly reserved for a probe that FAILS or returns an UNKNOWN
  awareness context (fail-closed preserved), with a precise, actionable message —
  never the generic "not DPI-aware."
- The DPI-unaware guest's native minimum-track size (`WM_GETMINMAXINFO`, answered
  in the guest's logical 96-DPI space) is converted to the physical-pixel
  contract at a single authoritative boundary, so the size-constraint /
  pane-overflow hardening stays correct for unaware guests on scaled monitors.
- Deterministic scaling math is added to `--selftest-geometry`; GuineaPig gains
  `--dpi unaw|system|per-monitor|per-monitor-v2` launcher modes; gated supervised
  scenarios cover capture acceptance at non-100% scaling.

## Capabilities

### New Capabilities
- `dpi-acceptance`: the revised DPI capture policy — known-DPI-unaware guests are
  accepted (physical-exact outer geometry), the per-target-monitor scale source,
  precise fail-closed for probe failure/unknown, and the centralized
  logical→physical min-track conversion preserving size-constraint containment.

### Modified Capabilities
- `ui-ux-hardening`: the "DPI-aware capture refusal at non-100% scaling"
  requirement is superseded: known-DPI-unaware guests are no longer refused; only
  an unverifiable probe is.

## Impact

- `Services/WindowShepherdService.cs`: capture gate (allow known-unaware, correct
  monitor-DPI probe, precise failure), `GetMonitorEffectiveDpi`,
  `ToPhysicalScaleForGuest` min-track conversion.
- `Services/SplitGeometry.cs`: `ScaleUnawareLogicalToPhysical` (single
  authoritative coordinate boundary) + self-test coverage.
- `NativeMethods.cs`: `GetDpiForMonitor` + `MDT_EFFECTIVE_DPI`,
  `USER_DEFAULT_SCREEN_DPI`.
- `tests/ValidationDriver/TabDock.GuineaPig`: `--dpi` launcher modes.
- `tests/ValidationDriver/.../Scenarios.Dpi.cs`: gated supervised scenarios.
- No new third-party dependencies; Shepherd/no-reparent architecture preserved.