## Purpose

Defines safe cleanup for supervised recovery transactions whose native work is
already durably complete, including HWND destruction and positive handle reuse.

## ADDED Requirements

### Requirement: Native-complete cleanup classifies the target explicitly
For a transaction at or beyond `NativeRecoveryComplete`, reconciliation SHALL
classify the original HWND as exact match, destroyed, positive replacement, or
unverifiable. Exact match may remove only the exact owned recovery token;
destroyed and positive replacement permit disk-only completion because native
work is already durable; unverifiable evidence SHALL remain pending.

#### Scenario: Exact completed target is cleaned without native recovery
- **WHEN** the original target still matches all durable identity fields
- **THEN** only the exact owned recovery token is removed if present and no placement, visibility, or DWM mutation repeats

#### Scenario: Reused HWND permits safe disk-only cleanup
- **WHEN** the original target is gone or the numeric HWND is positively reused by a different PID, process start, executable, or class
- **THEN** TabDock SHALL not touch the replacement and SHALL complete only durable disk cleanup

#### Scenario: Unverifiable completed target retains evidence
- **WHEN** a live target cannot be classified because a required identity probe is unavailable
- **THEN** TabDock SHALL retain the transaction and SHALL not remove any native property or repeat native recovery

### Requirement: A foreign token is never removed during completed cleanup
Reconciliation SHALL remove a recovery property only when its value equals the
durably recorded transaction token and the target is an exact identity match.

#### Scenario: Replacement foreign token remains untouched
- **WHEN** a positively replaced HWND carries a different recovery token
- **THEN** the foreign token remains untouched while disk-only completion proceeds
