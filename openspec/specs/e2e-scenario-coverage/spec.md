# e2e-scenario-coverage

## Purpose
The end-to-end scenario inventory that `all` runs, including the regression-specific scenarios added in the expand-e2e-coverage change (H2 oscillation bounds, H4 render liveness, H6 minimize-retains-tabs, and the picker/persistence/no-nesting behavioral coverage).
## Requirements
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
- **NOTES** the test page's 500ms blink can be throttled to ≥1s when the guest is momentarily not the OS-foreground window (Chrome background-timer throttling), so two 600ms-apart captures can land in the same blink phase and read as byte-identical (variance `0.0000`). This is the documented sampling-timing flake class, not a rendering regression — the harness re-samples with fresh frame pairs before failing, and still fails if every sample is flat or black.

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

### Requirement: A restored group's persisted layout survives its re-captured member being destroyed or hidden again
The suite SHALL include a scenario that persists a group, force-kills and relaunches TabDock so the group restores as an empty shell, re-captures a guest into that shell, and then destroys or tray-hides that guest — verifying the group's name and tab metadata still remain in `state.json` rather than being wiped. This scenario SHALL run as part of `all`.

#### Scenario: Re-emptying a restored group a second time does not wipe its persisted layout
- **WHEN** a group restored from a previous session's `state.json` is re-populated with a captured guest, and that guest is then destroyed (window closed) or hides itself (tray-style close), leaving the group empty again
- **THEN** the group's name and originally persisted tab metadata are still present in `state.json` afterward — the same guarantee `persist-kill` proves for a clean-exit empty shell, proven here for the member-destroyed/hidden path instead

### Requirement: A guest's self-minimize restore timer cannot outlive its own tab or container
The suite SHALL include a scenario that minimizes a captured guest via its own native minimize button, verifies the 200ms restore check brings the still-captured guest back inside its tab, and then releases that guest's tab — verifying the guest ends up at its own pre-capture placement and is not repositioned by any restore machinery tied to the now-defunct tab or container. This scenario SHALL run as part of `all`.

#### Scenario: Releasing a tab after a self-minimize restore leaves the guest alone
- **WHEN** a captured guest is minimized via its own title-bar minimize button and is then restored inside its tab by the restore-check delay, after which its tab is released (or its container is closed)
- **THEN** once that delay has fully elapsed, the released guest is at its own pre-capture placement — not forced back to a restored/repositioned state by a stale timer tied to the now-defunct tab or container, and no guest window is orphaned
- **NOTES** the "release before the delay fires" variant is unreachable with real input (the harness needs seconds to click the tab's context menu against a 200ms timer), so the scenario exercises the restore-first branch and verifies the guard holds after teardown

### Requirement: The launcher's empty-state hint tracks the actual group count
The suite SHALL include a scenario that verifies the launcher's "No groups yet" hint is visible when zero groups exist and hidden once a group exists. This scenario SHALL run as part of `all`.

#### Scenario: The hint appears with no groups and disappears once one exists
- **WHEN** TabDock is launched fresh with zero groups
- **THEN** the launcher's empty-state hint is visible (not offscreen/collapsed), and after a group is created the hint is no longer visible

### Requirement: New safety regressions SHALL have deterministic or supervised coverage
The suite SHALL include regression coverage for changed HDWP chaining and
failure, full-state recovery fixtures, corrupt/backup/version persistence,
monitor failure injection, bundle privacy, hung-guest probing, and semantic
persistence durability. Hardware/session/real-input limitations SHALL be
reported as blocked rather than represented as green.

#### Scenario: A hermetic safety regression is red
- **WHEN** a deterministic seam or fixture violates its contract
- **THEN** the self-test/CI qualification exits nonzero

#### Scenario: A required desktop test is unavailable
- **WHEN** hardware or supervised input is unavailable
- **THEN** the validation ledger records `BLOCKED_ENVIRONMENT` with the exact follow-up procedure

#### Scenario: A synthetic native probe starts from a defined buffer
- **WHEN** TabDock sends `WM_GETMINMAXINFO` to a captured guest for a dirty
  constraint refresh
- **THEN** the complete `MINMAXINFO` buffer is initialized before dispatch, and
  an indeterminate field cannot become a false container minimum
