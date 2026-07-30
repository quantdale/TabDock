# Tasks

## 1. Per-item restore resilience

- [ ] 1.1 In `PersistenceService.Load`, drop null entries from `state.Groups` after deserialize
- [ ] 1.2 Treat null `pg.Tabs` as an empty list
- [ ] 1.3 Wrap each group restore in its own try/catch so one malformed group can't discard the rest

## 2. Corrupt-file preservation

- [ ] 2.1 On deserialize failure, rename the bad file to `state.json.corrupt-<timestamp>` before returning empty
- [ ] 2.2 Keep the pre-overwrite file as `state.json.bak` in `Save`
- [ ] 2.3 Add a uniqueness suffix to the journal's `.corrupt` rename in `LoadJournal` (currently `IOException`-prone on timestamp collision)

## 3. Journal normalization

- [ ] 3.1 In `WindowShepherdService.LoadJournal`, normalize `file.Entries ??= new List<HiddenWindowEntry>()` before returning

## 4. Single-instance guard

- [ ] 4.1 Add a named `Global\TabDock` mutex in `App.OnStartup`; on contention, log and exit gracefully
- [ ] 4.2 Verify `Application_Exit` releases the mutex on all paths

## 5. Accent color validation

- [ ] 5.1 Validate persisted `AccentColor` with `ColorConverter.ConvertFromString` in `Load` (or in the `Group.AccentColor` setter); fall back to `#2196F3` on failure

## 6. Validation

- [ ] 6.1 `dotnet build TabDock.sln` clean
- [ ] 6.2 Manually verify: malformed `state.json` restores the good groups and preserves the bad file; `{"Entries": null}` journal is rescued-or-cleared once, not retried forever
