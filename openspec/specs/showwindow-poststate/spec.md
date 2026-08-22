# showwindow-poststate Specification

## Purpose
TBD - created by archiving change post-remediation-review-followup-2026-08-13. Update Purpose after archive.
## Requirements
### Requirement: ShowWindow success SHALL be determined from resulting native state

Production code SHALL NOT interpret the BOOL returned by `ShowWindow` as the
success status of the requested operation. Restore SHALL verify that the
result is visible, non-iconic, and non-zoomed; hide/show/release SHALL verify
resulting visibility against the requested state.

#### Scenario: Hidden restore with a benign false return succeeds

- **WHEN** a hidden window is restored and `ShowWindow(SW_RESTORE)` returns
  false because the window was previously hidden
- **THEN** the operation SHALL remain successful when the resulting state is
  restored, and SHALL not consume the later native-failure suppression slot

#### Scenario: Minimized restore is verified after the call

- **WHEN** a minimized guest is restored
- **THEN** the resulting iconic/zoomed state SHALL be checked before layout
  re-glue proceeds, and the window SHALL also be visible

#### Scenario: A restore that remains hidden fails

- **WHEN** `ShowWindow(SW_RESTORE)` returns but `IsWindowVisible` remains false
  for an otherwise normal, non-iconic window
- **THEN** restore SHALL be treated as a genuine failure

#### Scenario: Hide and release verify visibility

- **WHEN** hide, release/show, or intentional release/hide calls `ShowWindow`
- **THEN** the operation SHALL compare `IsWindowVisible` with the requested
  post-state

