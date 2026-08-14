## MODIFIED Requirements

### Requirement: One malformed group cannot discard the rest of the restore
`PersistenceService.Load` SHALL tolerate malformed entries in `state.json` on
a per-item basis: null group entries are dropped, a null `Tabs` list is
treated as empty, null or malformed nested tab records are skipped with a
bounded diagnostic, and a group that fails to restore does not prevent the
remaining groups or valid sibling tabs from restoring. A later save after
salvage SHALL retain every valid recovered tab/group and SHALL not be blocked
unless the primary document itself is unreadable, unsupported, or syntactically
corrupt under the existing quarantine contract.

#### Scenario: A state file with one bad group restores the good ones
- **WHEN** `state.json` contains three groups, one of which is malformed (null entry, null `Tabs`, or otherwise unrestorable)
- **THEN** the two well-formed groups are restored normally, the malformed one is skipped and logged, and the load does not throw

#### Scenario: A null tab does not discard valid tab siblings
- **WHEN** a group contains a valid tab, a null tab, and another valid tab
- **THEN** both valid tabs remain in the restored group and the null record is logged and ignored

#### Scenario: A malformed nested tab does not discard later groups
- **WHEN** one tab has an invalid JSON shape but a later group is valid
- **THEN** the invalid tab is skipped, the valid group and valid sibling tabs restore, and a later save retains them

#### Scenario: Salvage bounds the active index
- **WHEN** invalid tabs are removed from a group and the persisted active index exceeds the remaining tab count
- **THEN** the restored active index is clamped to a valid tab or zero for an empty shell

### Requirement: Root property classification follows the serializer casing contract
Manual classification of the schema version and groups properties SHALL be
case-insensitive in the same way as actual state deserialization.

#### Scenario: Root casing variants are equivalent
- **WHEN** the root uses `version`, `VERSION`, or `Version`, and `groups`, `GROUPS`, or `Groups`
- **THEN** classification and restoration apply the same supported/future/corrupt policy for each spelling
