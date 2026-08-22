# monitor-dpi-probing Specification

## Purpose
TBD - created by archiving change post-remediation-review-followup-2026-08-13. Update Purpose after archive.
## Requirements
### Requirement: Effective monitor DPI SHALL be queried through a DPI-aware API

Production and qualification code SHALL NOT call `GetDpiForMonitor` from a
PerMonitorV2 thread. Effective DPI for an arbitrary target monitor SHALL be
obtained through a PerMonitorV2 HWND associated with that monitor and
`GetDpiForWindow`, failing closed when the helper or query cannot be verified.

#### Scenario: A DPI-unaware target does not collapse the monitor probe to 96

- **WHEN** the target guest is DPI unaware and `GetDpiForWindow(target)` would
  return 96 on a scaled monitor
- **THEN** the monitor helper SHALL provide the target monitor's effective DPI
  for physical-pixel conversion

#### Scenario: Probe failure is conservative

- **WHEN** helper creation, monitor association, awareness verification, or
  `GetDpiForWindow` fails
- **THEN** the probe SHALL return unavailable and the caller SHALL fail closed
  or retain its existing bounded fallback according to its existing policy

#### Scenario: Conversion is deterministic without mixed-DPI hardware

- **WHEN** pure conversion tests supply 96, 120, 144, or 192 DPI
- **THEN** unaware logical dimensions SHALL convert to the expected physical
  dimensions while aware guests and failed probes remain unchanged

### Requirement: Known DPI-unaware guests SHALL remain capturable

Capture SHALL accept a guest whose DPI-awareness probe positively identifies
`DPI_UNAWARE`, including when the target monitor's effective DPI is not 96.
TabDock SHALL keep physical outer-window geometry as the positioning contract,
while documenting that Windows may bitmap-scale the unaware guest's content.
Unknown or failed awareness and monitor-DPI probes SHALL still fail closed.

#### Scenario: Known unaware capture is accepted on a valid scaled monitor

- **WHEN** awareness is known `DPI_UNAWARE` and the target monitor helper
  returns a valid effective DPI such as 144
- **THEN** capture SHALL be admitted, the outer geometry SHALL remain physical,
  and the content-rendering caveat SHALL not be reported as a capture refusal

#### Scenario: Unknown awareness remains refused

- **WHEN** the awareness context is zero, throws, or otherwise cannot be
  classified
- **THEN** capture SHALL be refused closed rather than treating unknown as
  known unaware or aware

#### Scenario: Unaware minimum track is converted once at the boundary

- **WHEN** an unaware guest reports a logical minimum track width of 500 at
  144 DPI
- **THEN** the centralized conversion SHALL produce 750 physical pixels;
  96 DPI SHALL remain 500 and aware guests SHALL remain unchanged

