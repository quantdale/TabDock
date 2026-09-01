# presentation-integrity Specification

## Purpose

Defines visual, foreground, native geometry, and bounded local z-order
invariants for Shepherd guests while TabDock-owned chrome and guest-originated
state transitions are active.
## Requirements
### Requirement: TabDock chrome SHALL not blank or unintentionally occlude the active guest

While a captured guest is presented, opening or using TabDock-owned transient
chrome — including the accent-color menu, workspace/group menu, rename editor,
split affordance/actions, tab context menus, capture surfaces, and owned
confirmation/result dialogs — SHALL keep the interaction surface reachable
without unintentionally replacing the guest content region with the opaque
container surface. Foreground/keyboard priority for TabDock chrome SHALL NOT
by itself imply that the entire container must visually cover the guest.

A deliberate in-window surface MAY consume or cover a documented portion of the
container only when the guest presentation is recomputed/managed for that
surface; the remainder of the guest SHALL stay live and correctly presented.
After the final relevant chrome interaction closes, the ordinary local
guest/container presentation SHALL reconcile once and settle without repeated
native churn.

#### Scenario: Accent menu stays usable without blanking guest content

- **WHEN** one guest is docked and the user opens, selects from, and dismisses the accent-color menu
- **THEN** the menu is visible/clickable, the guest remains captured and live, representative points in the guest content region resolve to the expected guest presentation rather than an opaque container cover, and closing the menu requires no tab switch to recover

#### Scenario: Rename keeps editor focus and guest rendering

- **WHEN** the user enters workspace rename, types, commits or cancels, and re-enters rename
- **THEN** keyboard input remains in the rename editor while the guest presentation stays visually healthy; completing rename restores ordinary interaction without a blank-content interval

#### Scenario: Split and capture chrome preserve presented content

- **WHEN** the user opens split actions or toggles the "+" capture workflow while a single guest or split pair is presented
- **THEN** the TabDock controls remain reachable and the guest(s) remain visibly presented according to the intentional layout for that surface, with no accidental full-content occlusion

### Requirement: Visual stacking and foreground ownership SHALL be distinct policies

The application SHALL model "which surface is visually above another" separately
from "which HWND receives keyboard foreground". A guard that suppresses guest
foreground stealing during TabDock interaction SHALL NOT automatically suppress
a visual-pairing repair that is required to keep the guest from being covered,
unless the current interaction explicitly requires that coverage.

Nested TabDock-owned chrome interactions SHALL preserve their ordering and SHALL
not restore the ordinary guest stack until the final interaction that requires
temporary presentation priority has ended.

#### Scenario: Rename owns keyboard without forcing guest foreground

- **WHEN** the rename editor has keyboard focus
- **THEN** TabDock does not steal focus back to the guest, yet the guest content remains visually presented according to the container/guest invariant

#### Scenario: Nested owned UI closes in either order

- **WHEN** one TabDock-owned interaction opens another owned popup/dialog and they close in either valid order
- **THEN** no early restore hides the remaining owned UI and the final close performs one bounded presentation reconciliation

### Requirement: A captured guest SHALL not silently escape its assigned presentation

A guest that is still a current strongly identified captured member SHALL NOT
silently become an independently roaming presentation while TabDock continues
to report it as docked. Guest-originated maximize, restore, fullscreen, move/size,
snap, or monitor-transfer transitions that move the guest outside its assigned
full-width or split pane SHALL be detected through bounded native
signals/observations and reconciled through the canonical presentation
authority.

Detection and repair SHALL preserve strong identity and generation checks and
SHALL be coalesced/idempotent. TabDock SHALL NOT continuously fight a guest
that refuses its assigned geometry. If safe containment cannot be maintained
under the Shepherd no-reparent/no-restyle contract, the product SHALL enter an
explicit bounded fail-closed outcome rather than silently claiming a healthy
dock or leaving an unbounded repair loop.

#### Scenario: Guest-originated maximize remains contained

- **WHEN** a captured guest invokes its own maximize transition
- **THEN** after bounded reconciliation it remains the same captured identity and is presented inside its assigned region rather than roaming as an unrelated maximized desktop window

#### Scenario: F11/custom fullscreen does not silently free the guest

- **WHEN** a captured application enters an application-defined fullscreen state without changing its captured identity
- **THEN** TabDock either safely contains the same guest in its assigned presentation or reports/executes the defined bounded fail-closed behavior; it does not silently leave the guest visually independent while claiming the dock is healthy

#### Scenario: Monitor transfer is reconciled or explicitly refused

- **WHEN** a still-captured guest is moved toward another monitor by a guest/system action while its TabDock container remains on the original assigned monitor
- **THEN** the guest is returned to the authoritative assigned presentation or the transition reaches the explicit bounded fail-closed outcome, with no identity confusion or endless position fight

### Requirement: Native observation of presentation drift SHALL be bounded

If additional native event coverage is needed for guest-originated geometry or
state changes, callbacks SHALL be rejected as early as possible unless they
refer to a current captured member, repeated events SHALL be coalesced before
presentation mutation, and metrics/tests SHALL prove that unrelated desktop
event storms do not create an unbounded dispatcher or P/Invoke workload.

The exact Windows event(s) are an implementation decision based on reproduction
evidence; this requirement SHALL NOT be interpreted as mandating
`EVENT_OBJECT_LOCATIONCHANGE` when another bounded signal is sufficient.

#### Scenario: Unrelated desktop geometry storm

- **WHEN** unrelated windows generate a high volume of geometry/state events while one TabDock guest is captured
- **THEN** unrelated callbacks are discarded through bounded membership routing and do not cause per-event guest layout/presentation mutations

#### Scenario: Repeated events for one captured guest

- **WHEN** one guest produces multiple equivalent presentation-change events in one dispatcher interval
- **THEN** the events coalesce to the minimum bounded reconciliation needed and a healthy final state does not trigger further writes

### Requirement: Local z-order repair SHALL remain scoped and topmost-safe

Presentation repair SHALL enforce only the local TabDock relationship necessary
for the active guest(s), container, and TabDock-owned chrome. It SHALL NOT
repeatedly reorder unrelated applications or make the TabDock container
permanently topmost. Ordinary and `WS_EX_TOPMOST` guests SHALL be handled
without assuming strict adjacency across z-order bands.

#### Scenario: Unrelated foreground window is respected

- **WHEN** the user activates an unrelated application outside TabDock
- **THEN** TabDock does not fight it for foreground/topmost status; returning to the captured guest/container restores only the local presentation invariant

#### Scenario: Topmost guest does not make container permanently topmost

- **WHEN** a captured guest carries `WS_EX_TOPMOST`
- **THEN** TabDock keeps its local guest/chrome behavior usable without converting the container into an always-on-top window or issuing an endless impossible adjacency repair

### Requirement: Workspace title SHALL be geometrically centered

In normal title-display mode, the visual midpoint of the workspace/group title
SHALL align with the container's horizontal client midpoint within the normal
layout rounding tolerance, independent of asymmetric left/right caption
controls. Rename mode SHALL use the same centered title region.

When width is constrained, the title/editor MAY trim or shrink according to
the responsive design, but SHALL NOT cover or disable required caption actions.

#### Scenario: Asymmetric controls do not shift the title

- **WHEN** workspace/split/add/window-control buttons consume more width on one side of the caption than the other
- **THEN** the title remains centered against the container, not merely centered inside the leftover column

#### Scenario: Long title degrades without covering controls

- **WHEN** a long workspace name is shown or edited in a narrow supported container
- **THEN** the title/editor trims or constrains itself while color, workspace, split, add, minimize, maximize/restore, and close actions remain reachable

### Requirement: Reported presentation regressions SHALL have non-vacuous qualification

Regression tests for these reports SHALL prove both the TabDock interaction and
the guest presentation. A test SHALL NOT pass solely because guest HWND
geometry matches the content host if the guest is actually covered by the
container. Where physical input/topology is required, qualification SHALL
remain supervised, capability-gated, and ownership/lease checked.

#### Scenario: Correct geometry but covered guest fails

- **WHEN** a guest rectangle exactly matches the content host but point ownership or client-render evidence shows the opaque container is above it
- **THEN** the presentation-integrity scenario fails rather than reporting a geometry-only pass

#### Scenario: Unsupported physical topology is honest

- **WHEN** a multi-monitor/topmost/fullscreen scenario cannot be safely exercised in the current environment
- **THEN** the run records the appropriate BLOCKED/SKIP capability outcome and deterministic policy coverage remains separate from physical qualification

### Requirement: Shepherd presentation SHALL remain contained across monitor and DPI transitions

A captured guest moved through a supported real monitor/DPI transition SHALL
remain the same logical member and verified native target generation where
applicable. Maximize/restore, single/split layout, local z-order, and point
ownership SHALL reconcile to the intended destination without unrelated monitor
jump, opaque-container cover, corrective tab switch, or continuous native fight.

#### Scenario: Guest transfers to a left-negative monitor
- **WHEN** a supervised transfer moves a controlled captured guest to a verified negative-X monitor
- **THEN** it remains captured, contained, visually live, and locally paired with its container

#### Scenario: Guest maximizes after mixed-DPI transfer
- **WHEN** the guest moves between different-DPI monitors and receives caption maximize or Win+Up
- **THEN** it stays in the same group/on the intended monitor and reconciles correctly after restore

### Requirement: Container title centering SHALL remain physical-DPI invariant

The visible title SHALL remain centered in the container physical width across
supported 96–192 DPI monitors, short/long names, narrow/default/wide widths, and
after transfer. Qualification SHALL measure the midpoint numerically.

#### Scenario: Long title moves from 120 DPI to 192 DPI
- **WHEN** the same container transfers between verified 120- and 192-DPI monitors
- **THEN** title midpoint remains within the documented physical-pixel tolerance of container midpoint at both observations

