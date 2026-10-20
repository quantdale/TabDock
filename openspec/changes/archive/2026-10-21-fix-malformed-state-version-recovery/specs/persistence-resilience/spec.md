## MODIFIED Requirements

### Requirement: Primary state classification SHALL control backup recovery

State restore SHALL distinguish missing, valid, supported-legacy, unsupported-future, corrupt, and unreadable primary state. A valid backup SHALL be used only when the primary is missing or has been proven corrupt and safely quarantined/preserved. Unreadable and future-version primary files SHALL remain in place and SHALL block later overwrites.

An absent Version SHALL retain supported legacy-version semantics. A present Version SHALL be a positive 32-bit JSON integer token; strings, null, booleans, objects, arrays, fractional numbers, out-of-range numbers, and nonpositive integers SHALL be classified corrupt without escaping the load boundary as type-conversion exceptions. A representable future integer version SHALL remain unsupported rather than corrupt.

If corruption cannot be preserved or a usable backup cannot be established, subsequent synchronous, asynchronous, emergency, and exit saves SHALL NOT overwrite unresolved primary/backup evidence with empty restored state. Unexpected load failures SHALL also leave later saves protected until an authoritative restore has been established. A successful backup restore SHALL preserve recovered group/tab intent through cleanup saves.

#### Scenario: Malformed primary recovers a valid backup

- **WHEN** `state.json` is malformed, is quarantined successfully, and `state.json.bak` is valid
- **THEN** the backup groups restore and the malformed evidence remains preserved

#### Scenario: An unreadable primary is not replaced by backup or empty state

- **WHEN** reading the primary fails with access/IO error
- **THEN** the backup is not treated as authoritative for this run, later saves are skipped, and the primary remains untouched

#### Scenario: A future version is preserved

- **WHEN** the primary contains a syntactically valid unsupported future schema version
- **THEN** it is reported as unsupported, neither it nor its state is rewritten, and no older backup silently replaces it

#### Scenario: Wrong-type Version cannot destroy a valid backup during cleanup

- **WHEN** the primary Version is each of a string, null, boolean, object, and array, a valid materialized backup exists, and emergency/exit saves follow restore
- **THEN** no type-conversion exception escapes, corrupt input is preserved before fallback, and recovered group/tab intent remains available rather than being replaced with an empty primary and malformed backup

#### Scenario: Invalid numeric versions are corruption

- **WHEN** Version is fractional, nonpositive, or outside the schema-integer range
- **THEN** restore classifies corruption and follows the same preservation and backup rules as malformed JSON

#### Scenario: Preservation failure blocks later saves

- **WHEN** malformed primary evidence cannot be quarantined or preserved and cleanup requests a save
- **THEN** the primary and existing backup remain unchanged and the failure is reported without an empty-state replacement

#### Scenario: Missing Version retains legacy compatibility

- **WHEN** an otherwise supported state file has no Version property
- **THEN** it restores under the supported legacy rules and retains its materialized metadata on the next save
