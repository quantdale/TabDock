# split-screen delta — vertical-split-screen (new capability)

## ADDED Requirements

### Requirement: Vertical two-pane split of exactly two captured guests
TabDock SHALL support displaying exactly two captured guests simultaneously in a LEFT/RIGHT vertical split over the container's content area. The split is initiated from a captured tab's context menu; the initiating tab becomes the LEFT pane. No top/bottom split, no grid, and no third simultaneously-visible guest are supported.

#### Scenario: Exactly two tabs auto-split
- **WHEN** a group has exactly two captured tabs and the user right-clicks one and selects `Split screen`
- **THEN** the initiating tab becomes the LEFT pane, the sole other tab becomes the RIGHT pane, both are visible, neither covers the other's pane, and both remain captured (tab count unchanged)

#### Scenario: Three or more tabs choose the RIGHT partner
- **WHEN** a group has three or more captured tabs and the user right-clicks the initiating tab and selects a partner from the `Split screen` submenu
- **THEN** the initiating tab becomes the LEFT pane, the chosen partner becomes the RIGHT pane, and any non-paired tab is hidden

#### Scenario: Fewer than two tabs cannot split
- **WHEN** a group has fewer than two eligible captured tabs
- **THEN** the `Split screen` command is shown disabled and no layout/visibility change occurs

### Requirement: Split state is identity-based and runtime-only
The split pair SHALL be represented by `CapturedWindow` references (not positional indices) so the pair identity survives tab reordering. Split state SHALL NOT be persisted to `state.json` (the pair is tied to live attached guests).

#### Scenario: Reordering tabs preserves the split pair
- **WHEN** a group with an active split has its tabs reordered
- **THEN** the same captured windows remain in their LEFT/RIGHT panes; none is swapped or destroyed by the reorder

### Requirement: Split geometry is a deterministic 50/50 split in physical pixels
Given the full content rect, the LEFT pane SHALL be `{left, top, left + Width/2, bottom}` and the RIGHT pane `{left + Width/2, top, right, bottom}` (integer division; odd widths give the right pane the extra pixel). No DPI conversion is introduced.

#### Scenario: Split panes use the content rect halves
- **WHEN** a two-member split is laid out in a content rect of any even or odd physical width
- **THEN** the LEFT guest occupies the left half, the RIGHT guest occupies the remaining half including any odd extra pixel, and both guests remain within the content rect

### Requirement: The container stays z-ordered below both split guests
During split the container SHALL remain below BOTH guests in the local TabDock
stack so clicks land on the guests and neither pane covers the TabDock chrome.
The foreground guest SHALL be above the partner, and the container SHALL be
paired immediately below the partner whenever the split layout is (re)applied;
the implementation SHALL NOT push the container behind unrelated desktop windows.

#### Scenario: Direct clicks deliver input to each pane
- **WHEN** the user clicks directly into a split member's pane
- **THEN** that member becomes the real foreground window and receives input, and both members remain glued to their panes

#### Scenario: Direct activation has a bounded reconciliation signal
- **WHEN** an unrelated external top-level window steals foreground and the user directly clicks a captured split member
- **THEN** the desktop reorder event for that activation re-establishes the local `foreground guest -> partner guest -> container` order through the existing Shepherd pairing policy, without polling, global bottoming, or a second z-order subsystem

### Requirement: Split lifecycle semantics
The following SHALL hold:
- Clicking a split member keeps the split active (the member becomes the focused member; its partner stays visible).
- Clicking a non-paired tab exits the split; that tab becomes the single visible guest and the former members are hidden (journal-safely).
- `Exit split screen` returns to single-visible-guest, keeps a sensible active member visible, hides the other via the journal-safe path, and does not release either guest.
- Popping out through TabDock's explicit tab action, or self-closing either split member, terminates the split cleanly and promotes the survivor to the single visible guest. Native guest move/size attempts are re-glued to the assigned pane and do not pop out.
- Minimizing the container hides both split members; restoring returns both to their panes with the split still active.
- The crash-recovery journal-before-hide ordering is preserved for every hide performed by a split transition.

#### Scenario: Guest self-close ends the split
- **WHEN** one split member's window is destroyed (self-close)
- **THEN** its tab is removed, the split terminates, and the surviving member becomes the normal full-width visible guest with no stale split state

#### Scenario: Minimize/restore preserves the split
- **WHEN** a split container is minimized and then restored
- **THEN** both members were hidden during minimize and both return to their correct panes after restore, with the split still active

### Requirement: Split is covered by automated ValidationDriver scenarios
The suite SHALL include pig-only, hermetic split scenarios (in `AllOrder`) covering: single-tab-disable, two-tab auto-split, three-tab partner selection, exit split, container resize, container move, minimize/restore, reorder, left pop-out, right pop-out, guest self-close, native move/resize re-glue, context-menu render stability, tab close affordances, middle-click pop-out, clicking a non-paired tab, direct-click input to both panes, and repeated enter/exit cycles. Pane membership SHALL be asserted from the content-host rect (LEFT/RIGHT halves within the existing tolerance), never via `GetParent`.

#### Scenario: Split scenarios run in `all`
- **WHEN** the ValidationDriver runs `all`
- **THEN** the split scenarios execute with fresh TabDock per scenario and assert pane geometry from the content host rect
