## ADDED Requirements

### Requirement: TabDock SHALL capture a known DPI-unaware guest normally
When TabDock successfully reads a target window's DPI-awareness context as
`DPI_AWARENESS_UNAWARE`, it SHALL proceed with capture (admit and position the
guest), even when the target monitor's effective DPI is not 96. The PerMonitorV2
caller's `SetWindowPos` pins the guest's outer rectangle in physical screen
pixels exactly regardless of the guest's awareness, so the guest's content may
appear DWM-bitmap-stretched (blurry) — which is its inherent standalone rendering
and SHALL NOT be represented as a capture failure.

#### Scenario: capturing a DPI-unaware window at non-100% scaling is accepted
- **WHEN** the user attempts to capture a window whose DPI-awareness context is known to be `DPI_AWARENESS_UNAWARE` on a monitor whose effective DPI exceeds 96
- **THEN** capture proceeds (the guest is admitted and positioned), because the PerMonitorV2 caller's physical-pixel `SetWindowPos` glues the guest's outer rectangle exactly regardless of the guest's awareness, and no "not DPI-aware / 100% scaling" refusal is shown

### Requirement: TabDock SHALL classify display scaling from the target's own monitor
The display-scaling decision for a window SHALL use the effective DPI
(`MDT_EFFECTIVE_DPI`) of the monitor that actually carries the target window via
`MonitorFromWindow`, never `GetDpiForSystem()` (the primary monitor), so
mixed-DPI multi-monitor setups are classified correctly.

#### Scenario: target sits on a differently-scaled secondary monitor
- **WHEN** the target is on a secondary monitor whose effective DPI differs from the primary's
- **THEN** capture is classified against the secondary monitor's effective DPI, so it is neither wrongly refused (unaware target on a 100% secondary while the primary is scaled) nor wrongly admitted while its own monitor is scaled

### Requirement: TabDock SHALL fail closed on a failed or unknown DPI probe
When TabDock cannot determine the target's DPI-awareness context (a null context
or a thrown probe) or the target monitor's effective DPI (no monitor, or
`GetDpiForMonitor` fails), it SHALL refuse capture with a precise, actionable
message that does not mislabel the problem as the window being "not DPI-aware."

#### Scenario: the awareness probe fails
- **WHEN** `GetWindowDpiAwarenessContext` returns null or throws
- **THEN** capture is refused with a message stating the window's DPI awareness could not be verified, and the diagnostic distinguishes "probe failed" from a known awareness class

#### Scenario: the monitor DPI probe fails
- **WHEN** `MonitorFromWindow` returns no monitor or `GetDpiForMonitor` does not yield a valid effective DPI
- **THEN** capture is refused with a message stating the target monitor's display scaling could not be determined, failing closed rather than admitting an unverifiable coordinate space

### Requirement: TabDock SHALL convert a DPI-unaware guest's min-track to physical pixels centrally
Because a DPI-unaware guest answers `WM_GETMINMAXINFO` in its logical 96-DPI
space, the physical minimum track size used for container containment SHALL be
computed by `SplitGeometry.ScaleUnawareLogicalToPhysical` from the guest's
reported logical minimum and the target monitor's effective DPI, rounding UP
(never under-estimating). This is the single authoritative logical→physical
boundary; a DPI-aware guest or a 96-DPI monitor SHALL use the reported value
unchanged (scale factor 1).

#### Scenario: an unaware guest with a native minimum on a scaled monitor is constrained correctly
- **WHEN** a DPI-unaware guest with a native `WM_GETMINMAXINFO` minimum is a member on a monitor whose effective DPI exceeds 96
- **THEN** the minimum used for containment is the guest's logical minimum scaled by (monitorEffectiveDpi / 96), rounded up, in physical pixels, so the pane-overflow hardening is not under-constrained

#### Scenario: an aware guest or 100% scaling is a strict no-op
- **WHEN** the guest is DPI-aware, or the target monitor's effective DPI is 96
- **THEN** the reported min-track value is used unchanged (scale factor 1), preserving the existing containment behavior for aware guests
