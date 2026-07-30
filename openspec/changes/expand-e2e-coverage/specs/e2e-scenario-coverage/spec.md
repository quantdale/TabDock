# e2e-scenario-coverage delta — expand-e2e-coverage (new capability)

## ADDED Requirements

### Requirement: Assertions only reference instrumentation present in committed source
No scenario SHALL assert on a log line, event, or signal that committed TabDock source does not emit. Before an assertion on log output is added or kept, the asserted substring SHALL be verified against the application source; assertions that fail this check SHALL be retargeted at an observable equivalent or removed.

#### Scenario: Stale instrumentation assertions are retargeted
- **WHEN** a scenario asserts on log output (e.g. `LAYOUT[*]` or `unhealthy` lines) that no committed code path can emit
- **THEN** the assertion is replaced by a directly observable equivalent (window geometry, `SHEPHERD[*]` lines that source does emit, pixel checks) or deleted — it never passes vacuously or fails unconditionally

### Requirement: Container minimize/restore retains all tabs (H6 regression)
The suite SHALL include a scenario that captures multiple guests, minimizes the container, restores it, and verifies every tab is retained and the active guest is re-docked. This scenario SHALL run as part of `all`.

#### Scenario: Minimizing and restoring a populated container keeps its tabs
- **WHEN** a container with two or more captured guests is minimized via its own minimize button and then restored
- **THEN** the tab count is unchanged, no guest was released or misclassified as a tray-close, and the previously active guest is docked over the content area again

### Requirement: Drag-reorder is bounded per drag (H2 regression)
The drag-reorder scenarios SHALL assert an upper bound on the number of `Reordered tab` log lines emitted during a single drag, and SHALL assert zero immediate flip-back pairs (a reorder X→Y for a tab followed directly by the reverse Y→X for the same tab).

#### Scenario: Oscillating reorder logic fails the scenario
- **WHEN** a tab is dragged across a neighbor's midpoint and the pointer is held near the slot boundary
- **THEN** the total reorder count stays within the bounded limit and no flip-back pair occurs — code that oscillates (H2) fails the scenario

### Requirement: Chromium guest rendering is verified across tab switches (H4 regression)
A browser scenario SHALL verify, via `PrintWindow`-based capture of the guest's own window (not screen-region `BitBlt`), that a Chromium-based guest is live-rendering after tab switches to it — hard assertions on brightness/variance against a deterministic local test page, not best-effort checks.

#### Scenario: A black or frozen browser tab fails the scenario
- **WHEN** a captured Chromium guest is switched away from and back to across repeated tab switches
- **THEN** a `PrintWindow` capture after each switch to the browser shows live content above the brightness/variance floor — a black or frozen frame fails the scenario

### Requirement: Held capture hotkey opens exactly one picker
The suite SHALL include a scenario that holds `Ctrl+Alt+G` (key repeat) and asserts exactly one capture picker exists. This scenario SHALL run as part of `all`.

#### Scenario: Holding the hotkey does not stack pickers
- **WHEN** `Ctrl+Alt+G` is held down long enough for keyboard auto-repeat to fire multiple times
- **THEN** exactly one capture picker window is open, and dismissing it leaves zero pickers

### Requirement: Releasing an inactive tab does not disturb the active tab
The suite SHALL include a scenario that pops out (or closes) an *inactive* tab of a multi-tab group and asserts the active tab selection is unchanged. This scenario SHALL run as part of `all`.

#### Scenario: Popping out a background tab keeps the user's current tab
- **WHEN** an inactive tab of a group with three or more tabs is popped out via its context menu
- **THEN** the previously active tab is still the active tab and its guest remains docked and visible

### Requirement: An already-captured window cannot be captured twice
The suite SHALL include a scenario that captures a window, reopens the capture picker, and verifies the already-captured window is absent from the picker or rejected on selection. This scenario SHALL run as part of `all`.

#### Scenario: Double-capture via the picker is refused
- **WHEN** the capture picker is reopened while a window is already captured in a group
- **THEN** that window does not appear as a selectable picker entry, or selecting it is rejected, and the group structure is unchanged

### Requirement: Persisted active-tab index survives restore and first save
The suite SHALL include a scenario that restores a persisted group with a non-zero active-tab index and verifies the first post-restore save does not reset that index. This scenario SHALL run as part of `all`.

#### Scenario: First save after relaunch preserves the active tab index
- **WHEN** state persisted with active tab index > 0 is restored on launch and the debounced save subsequently runs
- **THEN** `state.json` still records the original active tab index rather than resetting it to 0
