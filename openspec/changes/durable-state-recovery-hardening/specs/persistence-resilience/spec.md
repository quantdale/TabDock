## ADDED Requirements

### Requirement: The persistence backup SHALL be staged durably before primary replacement
`PersistenceService.CommitJson` SHALL treat the backup as a staged
sub-transaction of the save: when the primary exists, the next backup SHALL be
built by reading the primary once, durably flushing a candidate at a temporary
path, and atomically installing that candidate over `state.json.bak`. The
previous backup SHALL remain intact until the atomic install instant, and a
failure while constructing, flushing, or installing the backup candidate SHALL
leave both the previous backup and the primary untouched, SHALL log the
failure, and SHALL NOT advance the saved-content marker. When no primary
exists the backup stage SHALL be skipped so an existing valid backup is never
destroyed merely because the primary is absent. The single-writer gate,
latest-wins generation gate, unchanged-save optimization, and all load-side
classification/quarantine rules are unaffected.

#### Scenario: A failed backup candidate write preserves the previous backup
- **WHEN** writing or flushing the `state.json.bak.tmp` candidate fails during a save
- **THEN** `state.json.bak` still contains its previous content, `state.json` is untouched, the failure is logged, and a subsequent save succeeds and produces a fresh coherent backup

#### Scenario: A failed backup install preserves the previous backup
- **WHEN** the atomic replacement of `state.json.bak` fails
- **THEN** the primary is untouched and the previous backup remains usable whenever filesystem semantics permit

#### Scenario: A missing primary does not destroy the backup
- **WHEN** a save runs while `state.json` does not exist and a valid `state.json.bak` exists
- **THEN** the existing backup is preserved and the new primary is written normally

#### Scenario: A crash after the backup stage but before primary installation loses no state
- **WHEN** the process dies after the backup was installed but before the primary replacement completed
- **THEN** the primary still contains its previous content, the backup contains a copy of that same previous content, and no valid state was lost

#### Scenario: Stale temporary artifacts never become authoritative
- **WHEN** an interrupted save leaves `state.json.tmp` or `state.json.bak.tmp` behind and the application later loads or saves
- **THEN** load classification reads only `state.json` and `state.json.bak`, the next save truncates and reuses the temporary paths, and no temporary artifact is ever treated as durable state

#### Scenario: Backup staging failure is recoverable by retry
- **WHEN** any backup-stage boundary fails once and the fault is removed
- **THEN** the next save completes the full transaction (backup installed, primary installed) without requiring a restart
