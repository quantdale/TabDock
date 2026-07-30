# persistence-resilience

## Purpose
TBD — covers robustness of persisted group metadata in `state.json`, including tolerance for malformed entries, preservation of corrupt files, accent-color validation, and the single-instance guard that protects shared persistence files.

## Requirements

### Requirement: One malformed group cannot discard the rest of the restore
`PersistenceService.Load` SHALL tolerate malformed entries in `state.json` on a per-item basis: null group entries are dropped, a null `Tabs` list is treated as empty, and a group that fails to restore does not prevent the remaining groups from restoring.

#### Scenario: A state file with one bad group restores the good ones
- **WHEN** `state.json` contains three groups, one of which is malformed (null entry, null `Tabs`, or otherwise unrestorable)
- **THEN** the two well-formed groups are restored normally, the malformed one is skipped and logged, and the load does not throw

### Requirement: A corrupt state file is preserved before being replaced
When `state.json` cannot be deserialized, `PersistenceService` SHALL rename the unreadable file to a collision-safe `state.json.corrupt-<timestamp>` name before returning an empty state, and `Save` SHALL preserve the pre-overwrite file as `state.json.bak`, so a failed load is never silently followed by the evidence being overwritten with empty state.

#### Scenario: A corrupt state file survives the next save
- **WHEN** `state.json` is truncated or otherwise unparseable, TabDock starts, and any state change triggers a save
- **THEN** the original corrupt content still exists on disk under the `.corrupt-<timestamp>` (or `.bak`) name after the save completes

### Requirement: A persisted accent color that fails to parse falls back to the default
When a restored group's `AccentColor` string cannot be parsed by `ColorConverter.ConvertFromString`, the restore SHALL substitute the default `#2196F3` instead of propagating the invalid value, so a hand-edited or corrupt color can never produce a fully transparent, non-hit-testable container window.

#### Scenario: An invalid persisted color yields a normal container
- **WHEN** `state.json` contains a group with `"AccentColor": "not-a-color"`
- **THEN** the restored group uses the default accent color and its container renders and hit-tests normally

### Requirement: Only one TabDock instance runs at a time
`App.OnStartup` SHALL acquire a named `Global\TabDock` mutex; a second instance SHALL log the contention and exit gracefully instead of sharing `state.json` / `hidden-windows.json` with the first (last-writer-wins temp-file replacement would silently discard one instance's groups and can double-rescue journaled windows).

#### Scenario: A second instance exits without touching shared state
- **WHEN** TabDock is already running and a second instance is launched
- **THEN** the second instance logs the contention and exits without reading or writing `state.json` or `hidden-windows.json`, and the first instance is unaffected
