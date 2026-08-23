## ADDED Requirements

### Requirement: Picker-to-capture handoff SHALL carry strong selection evidence

The picker-to-capture handoff SHALL carry the available process-start and
window-class evidence alongside HWND, PID, and executable identity. A missing
required process-instance probe SHALL not be treated as a wildcard. The final
capture operation SHALL remain the authoritative native identity/admission gate
and SHALL fail closed before presentation mutation when identity is stale or
unverifiable.

#### Scenario: Process-start evidence is checked before picker submission

- **WHEN** a selected row's live process-start identity differs from the value
  captured at selection time
- **THEN** the row is rejected as stale and Shepherd performs no presentation
  mutation for it

#### Scenario: An inaccessible process is not admitted through the picker

- **WHEN** executable or process-start identity cannot be read for a selected
  target
- **THEN** the target is omitted or rejected with an actionable failure and no
  native capture mutation is attempted
