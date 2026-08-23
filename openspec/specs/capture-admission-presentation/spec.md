# capture-admission-presentation

## Purpose

Makes the canonical capture-admission decision and its current human-readable
reason observable at every capture entry point, including live health
transitions.

## Requirements

### Requirement: Capture admission SHALL be one observable canonical state

The application SHALL expose one capture-admission state containing whether
capture is allowed and the current human-readable reason. The state SHALL be
owned by the existing capture-admission authority, and every state change
including a reason change SHALL be observable without restarting TabDock.

#### Scenario: Admission transitions are projected live
- **WHEN** capture admission changes from allowed to blocked or from blocked to allowed
- **THEN** all active launcher/container capture surfaces update from the same state without restart

#### Scenario: Reason changes are not log-only
- **WHEN** the admission authority changes its reason while publishing a state transition
- **THEN** the current reason is available to the UI and accessibility layer, not only to diagnostics

### Requirement: Blocked admission SHALL disable or clearly block capture surfaces

When capture admission is blocked, the launcher Capture control and each
container Add window surface SHALL be disabled or clearly blocked, SHALL show
the current reason, and SHALL not advertise `Ctrl+Alt+G` as usable. Inline and
modal capture selection controls SHALL not appear functional while blocked.

#### Scenario: Launcher shows blocked capture
- **WHEN** capture admission is blocked because durable journal storage is unavailable
- **THEN** the launcher Capture control is disabled, its reason is accessible, and no usable capture shortcut hint is shown

#### Scenario: Inline Add window reflects blocked admission
- **WHEN** capture admission becomes blocked while a container is open
- **THEN** its Add window control and capture-selection action are disabled or clearly blocked and the reason is shown

#### Scenario: Retry success re-enables capture
- **WHEN** a pending WinEvent-monitor retry succeeds
- **THEN** the launcher and all open containers re-enable capture and replace the blocked reason with the healthy state

### Requirement: Shortcut availability SHALL remain distinct from capture admission

Failure to register the global capture shortcut SHALL not disable capture when
admission is allowed. The UI SHALL identify shortcut unavailability separately
from a blocked capture-admission reason.

#### Scenario: Capture shortcut registration fails while admission is allowed
- **WHEN** another process owns `Ctrl+Alt+G` but capture admission is healthy
- **THEN** capture buttons remain usable through their direct command and identify only the shortcut as unavailable

#### Scenario: Bounded monitor failure blocks capture independently
- **WHEN** the WinEvent monitor exhausts its bounded retry budget
- **THEN** capture is blocked with the monitor reason even if the global capture shortcut registered successfully

### Requirement: Admission presentation SHALL be automation-accessible

Capture controls and blocked-state explanations SHALL expose stable
AutomationIds, names, and help text sufficient to distinguish enabled capture,
blocked capture, and shortcut-only unavailability.

#### Scenario: Automation distinguishes the two unavailable states
- **WHEN** UI Automation inspects capture controls in each state
- **THEN** it can distinguish capture blocked, capture enabled with shortcut unavailable, and capture enabled with shortcut available
