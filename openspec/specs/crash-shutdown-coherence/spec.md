# crash-shutdown-coherence

## Purpose
Crash, shutdown, and fatal-error paths in TabDock remain internally consistent and do not corrupt persisted state, leave live hooks over unmanaged windows, or prompt the user during a teardown they cannot control.
## Requirements
### Requirement: The AppDomain unhandled-exception handler never touches UI-thread-affined state off-thread
`CurrentDomain_UnhandledException` SHALL NOT enumerate `GroupManager`'s collections, mutate the journal cache, or stop `DispatcherTimer`s from the faulting thread. When `e.IsTerminating` is true, the handler SHALL restrict itself to thread-safe logging; when false, it SHALL marshal the save/release/flush work onto the dispatcher with a bounded timeout, falling back to log-only if the dispatch cannot complete.

#### Scenario: A background-thread crash still saves state when the dispatcher is alive
- **WHEN** an unhandled exception is thrown on a non-UI thread while the UI dispatcher is responsive, and the exception is non-terminating
- **THEN** the crash-time save and emergency release run on the UI thread and complete without corrupting shared collections

#### Scenario: A terminating crash never corrupts the journal to save it
- **WHEN** a terminating unhandled exception fires on an arbitrary thread
- **THEN** the handler logs only — it does not race the UI thread's journal mutation — so `hidden-windows.json` on disk is never left torn by the crash handler itself

### Requirement: Session-ending release leaves coherent state
`Application_SessionEnding`'s emergency release SHALL leave the model and
presentation in a coherent post-release state: each group's just-released
member metadata and active index are preserved as persisted layout intent, open
containers clear released tabs and split references, released members are
removed from their groups (and thus from the HWND index), and the WinEvent
monitor is stopped. A cancelled logoff SHALL therefore not leave live hooks,
timers, or move-sync loops acting on unmanaged standalone windows, nor erase the
layout intent that a later save should preserve.

#### Scenario: A cancelled logoff leaves nothing half-captured
- **WHEN** `SessionEnding` releases all guests and the logoff is then cancelled by another application
- **THEN** no HWND remains in the captured index, the hooks and stale container dispatch are removed, released tabs and split references are cleared from the presentation, and the group's persisted tab metadata and active intent remain available for a later save

### Requirement: Every fatal shutdown path suppresses the close-confirm prompt
All shutdown/crash/startup-failure paths SHALL set `ContainerWindow.IsAppShuttingDown` before any container can be closed, so the Yes/No/Cancel close-confirm modal can never appear during a fatal shutdown.

#### Scenario: A startup failure after a restored container opened never prompts
- **WHEN** startup fails after at least one container window was restored, triggering `Shutdown(1)`
- **THEN** the container closes without showing the close-confirm dialog, and no Cancel can leave a window open while the process exits

### Requirement: Container-open failure rolls back the group and detaches the view model
If container construction or `Show()` throws (from either the capture-picker path or the new-group path), the group SHALL be removed (so it cannot be persisted and re-opened into a crash loop on the next launch), the constructed view model SHALL be `Detach()`ed (so it is not leaked via its `_group.PropertyChanged` subscription), and the failure SHALL surface as a `MessageBox`, not an escaping exception.

#### Scenario: A container-open failure is a one-time error, not a launch-loop
- **WHEN** `OpenContainer` throws after the group was created
- **THEN** the group is absent from the saved state on exit, the next launch does not retry the failed container, and the app keeps running

### Requirement: The close-confirm modal is re-entrancy-safe
While the close-confirm `MessageBox` is open (a nested dispatcher loop), WinEvent-driven member removals SHALL NOT re-enter `Close()` on the same window, and capture-picker requests SHALL be deferred until the prompt returns; the Yes path SHALL re-validate the tab count after the prompt closes.

#### Scenario: A guest destroying itself mid-prompt cannot re-enter Close
- **WHEN** the close-confirm prompt is open and the active guest destroys itself, emptying the tab list via the WinEvent handler
- **THEN** no second `Close()` is initiated on the window already inside `Closing`, and after the prompt returns the chosen action operates on the current (re-validated) tab list

### Requirement: Session-ending teardown SHALL be one-way and idempotent
Once `Application_SessionEnding` begins guest release, TabDock SHALL normalize
its model/container state, stop monitoring, and deliberately call `Shutdown`.
It SHALL NOT attempt to resume as an operational app if another process cancels
the Windows logoff/shutdown sequence.

#### Scenario: Session teardown cannot leave a hookless half-running app
- **WHEN** session-ending teardown has released and normalized captured guests
- **THEN** TabDock exits through its normal `Application_Exit` path and repeated exit cleanup is harmless
