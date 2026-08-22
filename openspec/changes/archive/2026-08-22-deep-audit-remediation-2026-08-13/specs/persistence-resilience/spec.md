## ADDED Requirements

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
