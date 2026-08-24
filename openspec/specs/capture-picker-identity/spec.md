# capture-picker-identity

## Purpose

Defines how a capture-picker selection remains attached to the same native
window across refreshes without weakening the final Shepherd admission gate.

## Requirements

### Requirement: Picker selection identity SHALL include process-instance and window-class evidence

The picker SHALL identify a refresh-stable selection by HWND, PID, process-start
identity, GUI window class, and executable path. Executable-path comparison SHALL
use Windows case-insensitive semantics. Mutable titles SHALL remain display data,
not identity. If required identity evidence is unavailable, a selection SHALL
not be carried forward as proven identity.

#### Scenario: The same window with a changed title keeps its selection

- **WHEN** refresh returns the same HWND, PID, process-start identity, class,
  and executable path with a new title
- **THEN** the row remains selected

#### Scenario: A same-HWND different-PID row does not inherit selection

- **WHEN** refresh returns the selected HWND with a different PID
- **THEN** the replacement row is unselected

#### Scenario: A recycled PID from a different process instance does not inherit selection

- **WHEN** refresh returns the same HWND and PID but a different nonzero
  process-start identity
- **THEN** the replacement row is unselected

#### Scenario: Executable path casing does not break continuity

- **WHEN** refresh returns the same identity with executable path casing that
  differs only by Windows path case
- **THEN** the row remains selected

#### Scenario: A changed window class does not inherit selection

- **WHEN** refresh returns the same HWND/PID/process instance/executable but a
  different GUI window class
- **THEN** the replacement row is unselected

### Requirement: Picker submission SHALL remain fail closed for stale selections

The picker SHALL discard selections whose rows disappear or whose final capture
admission identity no longer matches. Search filtering SHALL hide rows without
removing their selection from the authoritative candidate collection. The final
native capture transaction SHALL revalidate live identity and admission before
any guest presentation mutation.

#### Scenario: A selected target disappears before refresh

- **WHEN** a selected row is absent from the refreshed candidate set
- **THEN** no stale selection is submitted for that HWND

#### Scenario: A selected row is filtered out

- **WHEN** search text hides a previously selected candidate
- **THEN** the candidate remains selected in the master collection and is still
  included in submission

#### Scenario: A selected target becomes uncapturable before submit

- **WHEN** the selected HWND is captured elsewhere, replaced, or fails native
  admission after selection
- **THEN** the capture is rejected or reported as a failure and the replacement
  HWND is never mutated as the old selection
