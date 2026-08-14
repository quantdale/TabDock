## Purpose

Defines the user-visible close-group confirmation transaction so releasing
captured windows before graceful close cannot turn “Yes” into a release-only
operation or close a recycled native window.

## ADDED Requirements

### Requirement: Close-group Yes closes exactly the safely released applications
When the user confirms Yes in the close-group prompt, TabDock SHALL snapshot
each still-captured target's independent native identity, safely release the
group, and then post `WM_CLOSE` only to targets whose HWND, PID, GUI thread,
executable, class, and available process-start identity still match that
snapshot. The operation SHALL NOT depend on the live capture registry after
release.

#### Scenario: Successful Yes releases and closes every exact target
- **WHEN** every prompted guest has a matching strong identity and all releases complete safely
- **THEN** each guest is released and receives exactly the normal graceful-close request

#### Scenario: A release failure does not reinterpret Yes
- **WHEN** one or more guest releases remain pending or unverifiable
- **THEN** no post-release close request is sent and the existing fail-closed release evidence is retained

### Requirement: Released close-target verification is tri-state and fail-closed
The released-target verifier SHALL distinguish an exact match, a destroyed
target, a positively replaced/recycled target, and an unverifiable target.
Destroyed or positively replaced targets SHALL be skipped benignly without
mutating the replacement; unverifiable targets SHALL be skipped and logged.

#### Scenario: A recycled HWND is never closed
- **WHEN** the numeric HWND is reused by a different PID, thread, executable, class, or process instance before the close request
- **THEN** TabDock SHALL not post `WM_CLOSE` to the replacement

#### Scenario: Missing strong evidence fails closed
- **WHEN** any required released-target identity probe is unavailable
- **THEN** TabDock SHALL not post `WM_CLOSE` and SHALL retain an actionable bounded diagnostic
