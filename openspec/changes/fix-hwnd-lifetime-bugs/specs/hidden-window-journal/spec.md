# hidden-window-journal delta — fix-hwnd-lifetime-bugs

## MODIFIED Requirements

### Requirement: A newly-hidden guest's journal entry is written synchronously
`WindowShepherdService.JournalHide` SHALL write the updated journal to disk synchronously, immediately, on every call — NOT debounced. This is the one journal write that must precede an unpredictable future hard kill (`Process.Kill()`, Task Manager "End Task", `taskkill /F`), all of which invoke `TerminateProcess` and permit no user-mode code — including any `App.xaml.cs` exit/crash handler — to run afterward.

Additionally, `WindowShepherdService.Hide` SHALL complete the synchronous `JournalHide` write BEFORE issuing `ShowWindow(SW_HIDE)`. The previous order (hide first, journal second) left a force-kill window in which the guest was invisible on disk and in memory but had no journal entry — exactly the orphan the journal exists to rescue. The reversed order is safe: `RescueOrphanedWindows` re-showing an already-visible window is a documented harmless no-op.

#### Scenario: A hide is durable immediately, with no dependency on any later flush
- **WHEN** a guest becomes hidden (an inactive tab) and the process is hard-killed immediately afterward, before any other TabDock code runs
- **THEN** `hidden-windows.json` on disk already reflects that guest as hidden, because the write completed synchronously as part of hiding it — no debounce timer and no exit-handler flush is required for this to be true

#### Scenario: A force-kill between hide and journal-write can no longer strand an orphan
- **WHEN** `Hide()` is executing and the process is hard-killed at any point during it
- **THEN** the on-disk journal either has no entry yet (the guest is still visible — nothing to rescue) or already has the entry (the guest is hidden and will be rescued); there is no interleaving in which the guest is hidden but unjournaled

## ADDED Requirements

### Requirement: Release never hides a guest via an invalid capture-time placement
When capture-time `GetWindowPlacement` failed (leaving no valid `showCmd`), `WindowShepherdService.Release` SHALL NOT pass the zeroed placement's `showCmd` (0 == `SW_HIDE`) to `ShowWindow`. It SHALL instead show the guest with `SW_SHOW` after restoring its capture-time bounds, so a released guest is never left invisible with its journal entry already cleared.

#### Scenario: A guest captured while its placement was unreadable is still visible after release
- **WHEN** a guest whose `GetWindowPlacement` failed at capture time is released (pop out or tab close with show)
- **THEN** the guest window is restored to its capture-time bounds and shown with `SW_SHOW`, never hidden
