## Why

Persistence and journaling have several robustness gaps around malformed files and multi-instance use:

- **One malformed entry kills the whole restore.** `PersistenceService.Load` (`Services/PersistenceService.cs:129-168`) iterates `state.Groups` and `pg.Tabs` inside a single big try with no per-item null checks: `{"Tabs": null}` or a `[null]` group entry NREs and the catch returns whatever was accumulated — silently discarding *all* groups, not just the bad one.
- **Corrupt `state.json` evidence is destroyed.** A deserialize failure returns an empty list; the next debounced save (content differs from the null `_lastSavedJson`) overwrites the file with empty state — no backup, no `.corrupt` rename.
- **Null journal `Entries` permanently breaks crash rescue.** `WindowShepherdService.LoadJournal` (`Services/WindowShepherdService.cs:462`) only null-checks the root: `{"Entries": null}` makes `RescueOrphanedWindows` NRE *without deleting the journal*, so every launch retries and fails forever and orphans are never rescued. `LoadJournal`'s `File.Move(JournalPath, corruptPath)` (`:468`) can also throw `IOException` on a timestamp collision, escaping the `catch (JsonException)`.
- **No single-instance guard on the main app.** The Spike and ValidationDriver have named mutexes; TabDock itself doesn't. Two instances share `state.json` / `hidden-windows.json` temp+move paths → lost updates, dropped saves (sharing violation caught and logged), and double `RescueOrphanedWindows`.
- **Unvalidated persisted `AccentColor`** (`PersistenceService.cs:135`): a hand-edited/corrupt color string flows into `ColorToBrushConverter`'s `Brushes.Transparent` fallback → fully transparent, non-hit-testable container window.

## What Changes

- **Per-item restore resilience** — in `PersistenceService.Load`: sanitize `state.Groups` (drop null entries), treat null `pg.Tabs` as empty, and wrap each group restore in its own try/catch so one bad entry can't kill the rest.
- **Corrupt-file preservation** — on deserialize failure, rename the bad file to `state.json.corrupt-<timestamp>` before returning empty; keep the pre-overwrite file as `state.json.bak` in `Save`. Add a uniqueness suffix to the journal's `.corrupt` rename for the same reason.
- **Journal normalization** — in `LoadJournal`, normalize `file.Entries ??= new List<HiddenWindowEntry>()` before returning.
- **Single-instance guard** — add a named `Global\TabDock` mutex in `App.OnStartup`; on contention, log and exit gracefully (the pattern the project already mandates for its tools).
- **Color validation** — validate `AccentColor` with `ColorConverter.ConvertFromString` in `Load` (or in `Group.AccentColor`'s setter); fall back to the default `#2196F3` on failure.

## Capabilities

### New Capabilities
- `persistence-resilience`: Malformed-entry tolerance, corrupt-file preservation, accent-color fallback, and the single-instance guard for `state.json` persistence.

### Modified Capabilities
- `hidden-window-journal`: journal loading tolerates malformed entries (`Entries: null`) and the corrupt-file rename is collision-safe; rescue no longer wedges permanently on a malformed journal.

## Impact

- **Code**: `Services/PersistenceService.cs`, `Services/WindowShepherdService.cs`, `App.xaml.cs` (mutex), possibly `Models/Group.cs` (color validation site).
- **Behavior change**: a second TabDock instance now exits instead of silently clobbering the first instance's state — matches how the project's other tools already behave.
- **No schema changes**; `.corrupt`/`.bak` are additive artifacts only.
