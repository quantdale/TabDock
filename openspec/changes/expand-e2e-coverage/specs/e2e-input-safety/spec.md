# e2e-input-safety delta — expand-e2e-coverage (new capability)

## ADDED Requirements

### Requirement: Window identity is verified immediately before any input or window operation
Any test automation that sends input to, or performs a window operation on, a window SHALL verify that window's identity — owning process, class name, and title — immediately before the action. A window SHALL never be targeted by class name or title alone.

#### Scenario: Identity is re-verified before each action
- **WHEN** a scenario is about to click, type into, drag, or close a window
- **THEN** the scenario confirms the HWND still belongs to the expected process with the expected class and title, and aborts the action if verification fails

### Requirement: Input injection and window manipulation are scoped to test-owned windows
Test automation SHALL only inject input into, or manipulate, windows the test itself spawned or explicitly attached to for that run. Windows belonging to the user's pre-existing session SHALL NOT be targeted.

#### Scenario: A foreign window matching the search criteria is never touched
- **WHEN** a window matching the class/title a scenario is looking for exists but is owned by a process the test did not spawn (or explicitly attach to)
- **THEN** the scenario does not send it input, reposition it, or close it

### Requirement: Cleanup runs unconditionally
All window automation SHALL be wrapped so that detachment, release, and process cleanup run unconditionally — including on assertion failure or exception.

#### Scenario: A failed assertion still cleans up
- **WHEN** a scenario fails an assertion or throws mid-run after capturing/manipulating windows
- **THEN** its finally/cleanup path still restores cursor and window state, closes or kills every process it spawned, and leaves no captured window stranded

### Requirement: Single run, then report — no blind retries
A scenario SHALL NOT silently retry input injection or window operations against live windows without re-verifying window identity first. Each attempt runs once and reports; any retry re-runs discovery and identity verification from scratch.

#### Scenario: A retry re-discovers rather than reuses a stale HWND
- **WHEN** a scenario step must be attempted again after a failure
- **THEN** it re-discovers the target window and re-verifies process/class/title before sending any further input — it never reuses an HWND captured before the failure
