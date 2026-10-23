# accessibility-keyboard-completeness

## Purpose

Defines keyboard, focus, and UI Automation behavior for the release-candidate
launcher, picker, container chrome, tabs, split controls, and recovery states.

## Requirements

### Requirement: Critical product actions SHALL be keyboard complete

Critical actions SHALL be reachable in logical Tab order, expose a visible
keyboard-focus indication, and support the native Enter or Space activation
semantics appropriate to their control. Escape SHALL close transient capture or
rename surfaces, and default/cancel actions SHALL remain available without a
mouse.

#### Scenario: Keyboard-only capture flow completes

- **WHEN** a user tabs through the launcher, opens capture, searches, selects
  rows, and activates the primary action with the keyboard
- **THEN** the same capture request is raised as for the equivalent mouse flow

#### Scenario: Keyboard tab actions are available

- **WHEN** focus is in the container chrome and a user navigates the tab strip
  with Tab/arrow keys or invokes a focused close/pop-out action with Enter or
  Space
- **THEN** the selected tab, close, and pop-out behavior is available without
  requiring a mouse-preview event

#### Scenario: Split halves are distinguishable and activatable

- **WHEN** a user reaches the LEFT or RIGHT split half with keyboard focus and
  activates it
- **THEN** that member receives the same focus/resume operation as a mouse
  half-click, while the partner remains in the relationship

### Requirement: Important UIA elements SHALL expose stable names and state

Critical controls and list items SHALL expose meaningful, non-generic names,
stable existing AutomationIds, selected/disabled state, and help text for
blocked capture or recovery actions. LEFT and RIGHT split members SHALL be
distinguishable. Existing ValidationDriver AutomationIds SHALL NOT be removed or
silently renamed.

#### Scenario: Two capture rows are not announced with the same generic name

- **WHEN** assistive technology enumerates capture candidates
- **THEN** each row and its selection control can be identified by the window's
  title or another stable target-specific name

#### Scenario: Blocked capture explains the disabled state

- **WHEN** capture admission is unavailable
- **THEN** the launcher, inline picker, and standalone picker expose the
  canonical reason as visible/help text and the primary capture action is
  disabled without losing its name

#### Scenario: Recovery attention is announced as actionable state

- **WHEN** unresolved recovery evidence exists
- **THEN** the launcher exposes a warning state with the supported inspect and
  supervised-recovery commands and the state disappears only after discovery
  is clear

### Requirement: Keyboard focus feedback SHALL survive motion attachment

Critical styled inputs, picker rows, workspace tabs and split halves SHALL retain a visible keyboard-focus indication after motion attachment, reload and recycling. Focus feedback SHALL remain distinguishable while hover, checked selection or split-active feedback is also present, without requiring a pointer event or changing the action/selection model.

#### Scenario: Keyboard-only picker row gains focus after load

- **WHEN** a standalone or inline picker row gains keyboard focus after its feedback is attached
- **THEN** its focus border is visible even if its base border comes from a style trigger and the row is already checked or hovered

#### Scenario: Focus moves between split halves

- **WHEN** keyboard focus moves from LEFT to RIGHT on a loaded composite
- **THEN** the visible focus indication follows RIGHT while semantic active-member feedback and existing activation behavior remain independent and correct
