## ADDED Requirements

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
