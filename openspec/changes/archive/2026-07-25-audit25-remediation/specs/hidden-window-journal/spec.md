## ADDED Requirements

### Requirement: Journal mutations use an in-memory cache instead of re-reading disk per call
`WindowShepherdService` SHALL load `hidden-windows.json` into memory at most once per process lifetime (on first mutation), and SHALL serve subsequent `JournalHide`/`JournalClear` calls from that in-memory copy instead of re-reading the file from disk on every call.

#### Scenario: Repeated tab switches do not re-read the journal file from disk
- **WHEN** the user switches tabs multiple times in a session
- **THEN** `hidden-windows.json` is read from disk at most once (the first time a hide or clear occurs), not once per switch

### Requirement: A newly-hidden guest's journal entry is written synchronously
`WindowShepherdService.JournalHide` SHALL write the updated journal to disk synchronously, immediately, on every call — NOT debounced. This is the one journal write that must precede an unpredictable future hard kill (`Process.Kill()`, Task Manager "End Task", `taskkill /F`), all of which invoke `TerminateProcess` and permit no user-mode code — including any `App.xaml.cs` exit/crash handler — to run afterward.

#### Scenario: A hide is durable immediately, with no dependency on any later flush
- **WHEN** a guest becomes hidden (an inactive tab) and the process is hard-killed immediately afterward, before any other TabDock code runs
- **THEN** `hidden-windows.json` on disk already reflects that guest as hidden, because the write completed synchronously as part of hiding it — no debounce timer and no exit-handler flush is required for this to be true

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

### Requirement: On-disk journal format and rescue behavior are unchanged
Neither the in-memory caching nor the `JournalClear` debounce SHALL alter the `HiddenWindowJournalFile`/`HiddenWindowEntry` on-disk shape, the `.tmp`-then-`File.Move(overwrite:true)` atomic-write pattern, `LoadJournal`'s corrupt-file recovery behavior, or `RescueOrphanedWindows`' startup validation logic (HWND validity, owning PID, and exe path cross-check).

#### Scenario: Startup rescue behaves identically after this change
- **WHEN** TabDock starts up after a prior session ended in any way (graceful exit, managed exception, session ending, or a hard force-kill)
- **THEN** `RescueOrphanedWindows` restores exactly the windows it would have restored under the pre-AUDIT25-01, fully-synchronous behavior
