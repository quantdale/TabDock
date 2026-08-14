# hidden-window-journal

## Purpose
Captures the in-memory caching and synchronous write-durability semantics of
`WindowShepherdService`'s hidden-window journal (`hidden-windows.json`).

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
When capture-time `GetWindowPlacement` failed (leaving no valid `showCmd`), `WindowShepherdService.Release` SHALL NOT pass the zeroed placement's `showCmd` (0 == `SW_HIDE`) to `ShowWindow`. It SHALL instead show the guest with `SW_SHOW` after restoring its capture-time bounds, so a released guest is never left invisible with its journal entry already cleared.

#### Scenario: A guest captured while its placement was unreadable is still visible after release
- **WHEN** a guest whose `GetWindowPlacement` failed at capture time is released (pop out or tab close with show)
- **THEN** the guest window is restored to its capture-time bounds and shown with `SW_SHOW`, never hidden

### Requirement: Journal clear semantics are synchronous and idempotent
`WindowShepherdService.JournalClear` SHALL synchronously remove a matching
entry from the durable journal. It SHALL perform no disk write when no matching
entry exists. The actual-entry clear used by visible guests and the
intentional-hide cleanup used by `Release(show: false)` SHALL therefore have
the same synchronous durability boundary; there is no pending 300ms clear
timer. `FlushJournal` SHALL remain an idempotent synchronous lifecycle/safety
boundary for the cached journal.

#### Scenario: An actual clear is written once
- **WHEN** `JournalClear` finds the exact cached guest entry
- **THEN** it removes that entry and performs one durable journal write before
  returning

#### Scenario: A missing clear does not write
- **WHEN** `JournalClear` is called for a guest with no matching journal entry
- **THEN** it returns without changing the journal and without performing a
  disk write

#### Scenario: Intentional hide cleanup is durable before return
- **WHEN** a guest performs the intentional `Release(show: false)` path
- **THEN** the matching journal entry is synchronously cleared before the
  release transaction completes, so a later rescue cannot resurrect it

#### Scenario: Lifecycle flush is an idempotent boundary
- **WHEN** an exit or crash handler calls `FlushJournal` after journal
  mutations have already been synchronously committed
- **THEN** the flush completes safely and does not invent a debounce timer or
  require another delayed write

### Requirement: On-disk journal format remains stable and rescue is fail-safe and retryable
Neither the in-memory caching nor synchronous `JournalClear` SHALL alter the
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

### Requirement: Destructive journaled mutations SHALL revalidate HWND generation at native boundaries

After potentially slow journal I/O or a preceding native presentation mutation,
`WindowShepherdService` SHALL perform an immediate cheap generation check before
the next destructive native operation. Capture SHALL strongly revalidate PID,
GUI thread, class, executable, and process-start identity after
`JournalCapture` and before installing its per-capture token, then check the
new token before DWM mutation. Hide SHALL revalidate after `JournalHide` and
before `ShowWindow(SW_HIDE)`. Release and startup rescue SHALL stop on a
mismatch or preserve recovery evidence on an unverifiable result; exact token
removal SHALL be property-value scoped. The final check-to-native-call interval
is an unavoidable Win32 residual race and SHALL NOT be described as atomic.

#### Scenario: A recycled target after JournalHide is never hidden
- **WHEN** the HWND generation changes after the durable hide journal commit
- **THEN** `ShowWindow(SW_HIDE)` SHALL not be called for that numeric HWND and
  old journal cleanup SHALL not remove a newer generation's evidence

#### Scenario: Rescue stops after a replacement appears
- **WHEN** a valid v3 rescue identity passes its initial check but the target
  generation changes after placement restoration
- **THEN** visibility, DWM, and token-removal mutations SHALL not continue
  against the replacement HWND, and the old evidence SHALL be classified from
  the observed mismatch

### Requirement: Tokenless legacy evidence SHALL be recovered only by explicit supervision

Startup SHALL never mutate a v1 or v2 tokenless journal entry. The application
SHALL provide a read-only pending-evidence discovery command and a separate
user-initiated recovery command that requires entry selection, live top-level
window selection, validation of every historical field available, and explicit
confirmation. Before installing its external recovery property or performing
native work, recovery SHALL durably persist a versioned transaction containing
the source SHA/fingerprint, exact target identity, recovery mode, and a
cryptographically random nonzero token. Recovery SHALL revalidate its exact
generation before each placement, visibility, DWM, and token-removal operation
and SHALL resume interrupted transactions without repeating native work after a
durable native-complete marker. v1 recovery SHALL restore visibility only; v2
MAY restore recorded presentation state; v2 `DoNotRescue=true` SHALL preserve
intentional-hide semantics and SHALL NOT show or reposition the guest. Failed
or unresolved recovery SHALL retain the evidence, foreign recovery properties
SHALL never be removed, and retiring one entry SHALL preserve unresolved
siblings and unknown fields.

#### Scenario: Pending legacy evidence is not auto-mutated
- **WHEN** startup finds v1 or v2 journal bytes without the v3 per-HWND token
- **THEN** it SHALL preserve them as pending evidence and perform zero native
  presentation mutations until a user confirms a selected live target

#### Scenario: Supervised recovery retains an unresolved sibling
- **WHEN** one entry in a multi-entry pending file is successfully recovered
- **THEN** only that entry SHALL be durably marked and retired; unresolved
  sibling entries and unknown JSON fields SHALL remain available

#### Scenario: Interrupted supervised recovery resumes by phase
- **WHEN** a supervised recovery process is killed after its durable prepared,
  token, or partial-presentation phase
- **THEN** the next supervised invocation recognizes only an exact matching
  transaction/token, requires explicit confirmation, resumes the missing safe
  phase, and retains evidence if identity cannot be revalidated

#### Scenario: Intentional-hide evidence is never resurrected
- **WHEN** a selected historical v2 entry contains `DoNotRescue=true`
- **THEN** supervised recovery does not call `ShowWindow` or restore placement;
  it only cleans the historically required presentation transition state and
  retires the entry after successful identity validation
