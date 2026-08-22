# persistence-resilience

## Purpose
Covers robustness of persisted group metadata in `state.json`, including tolerance for malformed entries, unique runtime identifiers, preservation of corrupt or unreadable files, accent-color validation, and the single-instance guard that protects shared persistence files.
## Requirements
### Requirement: One malformed group cannot discard the rest of the restore
`PersistenceService.Load` SHALL tolerate malformed entries in `state.json` on a per-item basis: null group entries are dropped, a null `Tabs` list is treated as empty, and a group that fails to restore does not prevent the remaining groups from restoring.

#### Scenario: A state file with one bad group restores the good ones
- **WHEN** `state.json` contains three groups, one of which is malformed (null entry, null `Tabs`, or otherwise unrestorable)
- **THEN** the two well-formed groups are restored normally, the malformed one is skipped and logged, and the load does not throw

### Requirement: Restored group identifiers are unique
`PersistenceService.Load` SHALL retain every otherwise-restorable group while
replacing an empty or duplicate persisted group ID with a newly generated
unique ID. The replacement SHALL be logged, and the first occurrence of a
valid ID SHALL remain unchanged.

#### Scenario: Duplicate group IDs do not collapse restored groups
- **WHEN** `state.json` contains multiple valid groups with the same ID, or a group with an empty ID
- **THEN** all valid groups are restored, each has a unique non-empty ID, and the later duplicate or empty ID is replaced and logged

### Requirement: A corrupt state file is preserved before being replaced
When `state.json` cannot be deserialized, `PersistenceService` SHALL rename the unreadable file to a collision-safe `state.json.corrupt-<timestamp>` name before returning an empty state, and `Save` SHALL preserve the pre-overwrite file as `state.json.bak`, so a failed load is never silently followed by the evidence being overwritten with empty state.

#### Scenario: A corrupt state file survives the next save
- **WHEN** `state.json` is truncated or otherwise unparseable, TabDock starts, and any state change triggers a save
- **THEN** the original corrupt content still exists on disk under the `.corrupt-<timestamp>` (or `.bak`) name after the save completes

### Requirement: An unreadable existing state file is not overwritten by an empty fallback
When an existing `state.json` cannot be read because of an I/O or access
failure, `PersistenceService.Load` SHALL return an empty restore result for the
current run but mark the load as unsafe. Any later `Save` in that process SHALL
skip replacing the unreadable file with empty state and SHALL log the reason.
Parseable corruption remains handled by the quarantine requirement above.

#### Scenario: A read failure cannot erase potentially valid state
- **WHEN** `state.json` exists but a read/access failure prevents `PersistenceService.Load` from inspecting it, and a state change or exit path later requests a save
- **THEN** the save is skipped, the original unreadable file is not replaced by an empty state, and the failure is logged

### Requirement: A persisted accent color that fails to parse falls back to the default
When a restored group's `AccentColor` string cannot be parsed by `ColorConverter.ConvertFromString`, the restore SHALL substitute the default `#2196F3` instead of propagating the invalid value, so a hand-edited or corrupt color can never produce a fully transparent, non-hit-testable container window.

#### Scenario: An invalid persisted color yields a normal container
- **WHEN** `state.json` contains a group with `"AccentColor": "not-a-color"`
- **THEN** the restored group uses the default accent color and its container renders and hit-tests normally

### Requirement: Only one product-mutating TabDock owner runs at a time
Normal `App.OnStartup` and the mutating `--recover-pending` command SHALL
acquire the same named `Global\TabDock` product-mutation lease. A second
normal or recovery process SHALL log the contention and exit gracefully
instead of sharing `state.json` / `hidden-windows.json` with the first
(last-writer-wins temp-file replacement would silently discard one instance's
groups, double-rescue journaled windows, or race pending-entry retirement).
Read-only diagnostic commands SHALL remain usable without this lease.

#### Scenario: A second mutating owner exits without touching shared state
- **WHEN** TabDock or supervised recovery already owns the product-mutation
  lease and another normal or recovery process is launched
- **THEN** the second process logs the contention and exits without reading or
  writing shared state or pending evidence, and the first owner is unaffected

#### Scenario: Read-only diagnostics remain independent
- **WHEN** TabDock or supervised recovery owns the product-mutation lease
- **THEN** `--version`, `--doctor`, `--pending-recovery`, and
  `--support-bundle` can still perform their existing read-only contract

### Requirement: Primary state classification SHALL control backup recovery
`PersistenceService.Load` SHALL distinguish missing, valid, supported-legacy,
unsupported-future, corrupt, and unreadable primary state. A valid backup MAY
be used only when the primary is missing or has been proven corrupt and safely
quarantined/preserved. Unreadable and future-version primary files SHALL remain
in place and SHALL block later overwrites.

#### Scenario: Malformed primary recovers a valid backup
- **WHEN** `state.json` is malformed, is quarantined successfully, and `state.json.bak` is valid
- **THEN** the backup groups restore and the malformed evidence remains preserved

#### Scenario: An unreadable primary is not replaced by backup or empty state
- **WHEN** reading the primary fails with access/IO error
- **THEN** the backup is not treated as authoritative for this run, later saves are skipped, and the primary remains untouched

#### Scenario: A future version is preserved
- **WHEN** the primary contains a syntactically valid unsupported future schema version
- **THEN** it is reported as unsupported, neither it nor its state is rewritten, and no older backup silently replaces it

### Requirement: Supported older state SHALL migrate explicitly
The current schema version SHALL be explicit; supported older versions SHALL
migrate in memory and future versions SHALL never be silently downgraded.

#### Scenario: Version one survives the first save without field loss
- **WHEN** a version-one fixture loads and a later save occurs
- **THEN** it is written as the current version with all supported group/tab fields preserved

### Requirement: Unmaterialized empty groups SHALL NOT accumulate
Fresh group shells with neither live members nor persisted tab metadata SHALL
not be written to `state.json`. Valid legacy records with no tabs SHALL be
skipped during restore, while groups containing persisted tab metadata SHALL
remain recoverable as empty-at-runtime layout intent.

#### Scenario: A fresh empty shell is omitted from a save
- **WHEN** a save contains a new empty group and a group with persisted tab metadata
- **THEN** only the materialized group is written

#### Scenario: A legacy empty record is ignored without dropping a sibling
- **WHEN** a valid state contains one zero-tab group and one group with a persisted tab
- **THEN** the zero-tab group is not restored and the materialized group is restored

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
