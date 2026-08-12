# hidden-window-journal

## Purpose
Captures the in-memory caching and write-durability semantics of `WindowShepherdService`'s hidden-window journal (`hidden-windows.json`), balancing crash-safety for hides against write-amplification for clears.

## Requirements

### Requirement: Journal mutations use an in-memory cache instead of re-reading disk per call
`WindowShepherdService` SHALL load `hidden-windows.json` into memory at most once per process lifetime (on first mutation), and SHALL serve subsequent `JournalHide`/`JournalClear` calls from that in-memory copy instead of re-reading the file from disk on every call.

#### Scenario: Repeated tab switches do not re-read the journal file from disk
- **WHEN** the user switches tabs multiple times in a session
- **THEN** `hidden-windows.json` is read from disk at most once (the first time a hide or clear occurs), not once per switch

### Requirement: A newly-hidden guest's journal entry is written synchronously
`WindowShepherdService.JournalHide` SHALL write the updated journal to disk synchronously, immediately, on every call — NOT debounced. This is the one journal write that must precede an unpredictable future hard kill (`Process.Kill()`, Task Manager "End Task", `taskkill /F`), all of which invoke `TerminateProcess` and permit no user-mode code — including any `App.xaml.cs` exit/crash handler — to run afterward.

Additionally, `WindowShepherdService.Hide` SHALL complete the synchronous `JournalHide` write BEFORE issuing `ShowWindow(SW_HIDE)`. The previous order (hide first, journal second) left a force-kill window in which the guest was invisible on disk and in memory but had no journal entry — exactly the orphan the journal exists to rescue. The reversed order is safe: `RescueOrphanedWindows` re-showing an already-visible window is a documented harmless no-op.

If that synchronous journal commit fails, `Hide` SHALL log the failure and leave the guest visible; it SHALL NOT issue `ShowWindow(SW_HIDE)` without a durable recovery entry.

#### Scenario: A hide is durable immediately, with no dependency on any later flush
- **WHEN** a guest becomes hidden (an inactive tab) and the process is hard-killed immediately afterward, before any other TabDock code runs
- **THEN** `hidden-windows.json` on disk already reflects that guest as hidden, because the write completed synchronously as part of hiding it — no debounce timer and no exit-handler flush is required for this to be true

#### Scenario: A force-kill between hide and journal-write can no longer strand an orphan
- **WHEN** `Hide()` is executing and the process is hard-killed at any point during it
- **THEN** the on-disk journal either has no entry yet (the guest is still visible — nothing to rescue) or already has the entry (the guest is hidden and will be rescued); there is no interleaving in which the guest is hidden but unjournaled

#### Scenario: A failed hide journal commit leaves the guest visible
- **WHEN** the synchronous `JournalHide` write fails
- **THEN** `Hide` records the failure and does not hide the guest, preserving a visible and recoverable state

### Requirement: Release never hides a guest via an invalid capture-time placement
When capture-time `GetWindowPlacement` failed (leaving no valid `showCmd`), `WindowShepherdService.Release` SHALL NOT pass the caller-initialized or otherwise invalid placement's `showCmd` (which could be `0 == SW_HIDE`) to `ShowWindow`. It SHALL instead show the guest with `SW_SHOW` after restoring its capture-time bounds, so a released guest is never left invisible with its journal entry already cleared.

#### Scenario: A guest captured while its placement was unreadable is still visible after release
- **WHEN** a guest whose `GetWindowPlacement` failed at capture time is released (pop out or tab close with show)
- **THEN** the guest window is restored to its capture-time bounds and shown with `SW_SHOW`, never hidden

### Requirement: A journal clear for a guest that ends up visible is debounced
When `JournalClear` is invoked for a guest that ends up genuinely visible (the `PositionAndShow` and `Release(show: true)` call sites), `WindowShepherdService` SHALL coalesce rapid calls into a single debounced disk write (~300ms after the last call in a burst), mirroring `GroupManager.RequestSave`'s existing `DispatcherTimer`-based coalescing pattern (`Services/GroupManager.cs:54-67`), instead of writing synchronously on every call. This is safe specifically because the guest is genuinely visible at the moment of the call — see the following requirement for the one call site where that does not hold.

#### Scenario: Rapid tab switching produces at most one clear-write per burst
- **WHEN** the user switches between tabs several times within the debounce window (e.g. three switches within 500ms), each switch clearing the newly-active tab's journal entry via `PositionAndShow`
- **THEN** the in-memory journal reflects every clear in order, but `hidden-windows.json` on disk is written at most once for the whole burst of clears, after the debounce interval elapses with no further activity

#### Scenario: A force-kill mid-debounce causes at most a harmless redundant rescue, never a missed one
- **WHEN** a guest's journal entry was cleared via `PositionAndShow` or `Release(show: true)` (the guest became/stayed visible) but the process is hard-killed before the debounce interval elapses, leaving the stale "hidden" entry on disk
- **THEN** the next startup's rescue re-shows that guest's window even though it was already visible — a harmless no-op — and no guest that should have been rescued is ever skipped as a result of this debounce

### Requirement: A journal clear for a guest that ends up intentionally hidden is synchronous, never debounced
`WindowShepherdService.Release`'s guest-initiated-hide path (`show: false` — tray-style close) SHALL clear the guest's journal entry synchronously, immediately (`JournalClear(hwnd, immediate: true)`), bypassing the debounce entirely. This call site is NOT safe to debounce: unlike `PositionAndShow`/`Release(show: true)`, the guest ends up intentionally hidden, not visible, so a stale "hidden" entry surviving a crash would be indistinguishable from a real orphan and would cause `RescueOrphanedWindows` to incorrectly un-hide a window the user deliberately hid — not a harmless no-op.

#### Scenario: A guest that hides itself is never resurrected by a later rescue, even under a worst-case race
- **WHEN** a guest was previously journaled as hidden (an earlier inactive-tab hide), is switched back to active (its clear now debounced-pending), and then hides itself (guest-initiated, tray-style close) before that debounced clear has fired, and the process is force-killed immediately after the self-hide completes
- **THEN** the on-disk journal has no entry for that guest by the time of the kill (the self-hide's immediate clear flushed the current in-memory state synchronously, superseding whatever debounce was pending), so the next startup's rescue does not act on it at all, and the guest's window remains hidden after the relaunch

### Requirement: Crash and exit paths force an immediate flush of any pending clear
`WindowShepherdService` SHALL expose a synchronous flush operation that stops any pending debounce timer and writes the current in-memory journal state to disk immediately. All five exit/crash paths in `App.xaml.cs` (`Application_Exit`, `Application_DispatcherUnhandledException`, `CurrentDomain_UnhandledException`, `Application_SessionEnding`, and the early-startup-failure path) SHALL call this flush operation. This exists to keep graceful exits and managed exceptions tidy (no lingering stale-but-harmless clear pending across a restart) — it is not what makes the force-kill case safe, since `JournalHide` never leaves anything pending for it to rescue in the first place.

#### Scenario: A designated exit path flushes a pending clear before the process ends
- **WHEN** a designated crash/exit handler runs while a `JournalClear` write is still pending in its debounce window
- **THEN** the flush completes synchronously before that handler returns, so `hidden-windows.json` on disk reflects the latest clear even though the debounce timer never fired on its own

#### Scenario: Flush is a no-op when nothing is pending
- **WHEN** a designated crash/exit handler runs and no journal mutation is pending
- **THEN** the flush operation completes without performing an additional disk write

### Requirement: On-disk journal format remains stable and rescue is fail-safe and retryable
Neither the in-memory caching nor the `JournalClear` debounce SHALL alter the
`HiddenWindowJournalFile`/`HiddenWindowEntry` on-disk shape, the `.tmp`-then-
`File.Move(overwrite:true)` atomic-write pattern, or `LoadJournal`'s
corrupt-file recovery behavior. `RescueOrphanedWindows` SHALL validate each
entry's HWND, owning PID, and exe path before touching it, and SHALL verify that
the window is visible after `ShowWindow` before consuming the entry. A valid
entry that remains hidden SHALL be retained for a later startup retry; invalid
or recycled identities SHALL be discarded.

Two robustness gaps in the existing behavior are closed (format unchanged, tolerance increased): `LoadJournal` SHALL normalize a deserialized null `Entries` to an empty list before returning, so a syntactically valid `{"Entries": null}` journal can no longer wedge rescue into a permanent fail-and-retry loop; and `LoadJournal`'s corrupt-file rename SHALL use a collision-safe name so the rename itself cannot throw out of the `JsonException` recovery path.

#### Scenario: Startup rescue consumes only entries that were visibly restored
- **WHEN** TabDock starts up after a prior session ended in any way (graceful exit, managed exception, session ending, or a hard force-kill)
- **THEN** `RescueOrphanedWindows` restores and removes entries for identity-valid windows that become visible, discards invalid or recycled identities, and retains identity-valid windows that could not be made visible for a later retry

#### Scenario: A journal with null Entries is handled once, not retried forever
- **WHEN** `hidden-windows.json` is syntactically valid JSON but has `"Entries": null`
- **THEN** it is treated as an empty journal (or quarantined as corrupt exactly once) — rescue completes, the file is not left in place to fail identically on every subsequent launch, and no orphan that IS correctly journaled is skipped because of it
