## ADDED Requirements

### Requirement: New safety regressions SHALL have deterministic or supervised coverage
The suite SHALL include regression coverage for changed HDWP chaining and
failure, full-state recovery fixtures, corrupt/backup/version persistence,
monitor failure injection, bundle privacy, hung-guest probing, and semantic
persistence durability. Hardware/session/real-input limitations SHALL be
reported as blocked rather than represented as green.

#### Scenario: A hermetic safety regression is red
- **WHEN** a deterministic seam or fixture violates its contract
- **THEN** the self-test/CI qualification exits nonzero

#### Scenario: A required desktop test is unavailable
- **WHEN** hardware or supervised input is unavailable
- **THEN** the validation ledger records `BLOCKED_ENVIRONMENT` with the exact follow-up procedure

#### Scenario: A synthetic native probe starts from a defined buffer
- **WHEN** TabDock sends `WM_GETMINMAXINFO` to a captured guest for a dirty
  constraint refresh
- **THEN** the complete `MINMAXINFO` buffer is initialized before dispatch, and
  an indeterminate field cannot become a false container minimum
