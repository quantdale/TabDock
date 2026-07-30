# hidden-window-journal delta — harden-persistence-and-journal

## MODIFIED Requirements

### Requirement: On-disk journal format and rescue behavior are unchanged
Neither the in-memory caching nor the `JournalClear` debounce SHALL alter the `HiddenWindowJournalFile`/`HiddenWindowEntry` on-disk shape, the `.tmp`-then-`File.Move(overwrite:true)` atomic-write pattern, `LoadJournal`'s corrupt-file recovery behavior, or `RescueOrphanedWindows`' startup validation logic (HWND validity, owning PID, and exe path cross-check).

Two robustness gaps in the existing behavior are closed (format unchanged, tolerance increased): `LoadJournal` SHALL normalize a deserialized null `Entries` to an empty list before returning, so a syntactically valid `{"Entries": null}` journal can no longer wedge rescue into a permanent fail-and-retry loop; and `LoadJournal`'s corrupt-file rename SHALL use a collision-safe name so the rename itself cannot throw out of the `JsonException` recovery path.

#### Scenario: Startup rescue behaves identically after this change
- **WHEN** TabDock starts up after a prior session ended in any way (graceful exit, managed exception, session ending, or a hard force-kill)
- **THEN** `RescueOrphanedWindows` restores exactly the windows it would have restored under the pre-AUDIT25-01, fully-synchronous behavior

#### Scenario: A journal with null Entries is handled once, not retried forever
- **WHEN** `hidden-windows.json` is syntactically valid JSON but has `"Entries": null`
- **THEN** it is treated as an empty journal (or quarantined as corrupt exactly once) — rescue completes, the file is not left in place to fail identically on every subsequent launch, and no orphan that IS correctly journaled is skipped because of it
