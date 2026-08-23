## Purpose

Provides reliable previous/next TabDock tab navigation when keyboard focus is
inside an independent shepherded guest window, without becoming a desktop-wide application switcher.

## ADDED Requirements

### Requirement: Global tab navigation SHALL use the reserved PageUp/PageDown shortcuts

TabDock SHALL register `Ctrl+Alt+PageUp` for previous TabDock-tab navigation
and `Ctrl+Alt+PageDown` for next TabDock-tab navigation, with repeated-key
suppression. It SHALL retain local `Ctrl+Tab` and `Ctrl+Shift+Tab` behavior.

#### Scenario: Global next and previous navigate in the shared tab order
- **WHEN** a scoped global next or previous shortcut is received for a group with multiple tabs
- **THEN** the same canonical tab-navigation decision used by local Ctrl+Tab navigation selects the corresponding next or previous target, including wraparound

#### Scenario: Presented split preserves pair cycling
- **WHEN** global navigation is received while a split pair is presented
- **THEN** navigation cycles the pair members using LEFT/RIGHT identity semantics and does not use presentation-strip indices

#### Scenario: Dormant split preserves relationship semantics
- **WHEN** global navigation is received while a split relationship is dormant
- **THEN** navigation follows the ordinary authoritative tab order and any member activation uses the existing dormant-resume path

### Requirement: Global navigation SHALL be scoped to proven TabDock foreground context

Global navigation SHALL act only when the foreground context can be proven to
be a current captured guest or its owning live TabDock container/chrome. When
the foreground belongs to an unrelated application, or identity/group/container
resolution is stale, recycled, ambiguous, or unavailable, the handler SHALL
perform no navigation and SHALL not select an arbitrary last-used group.

#### Scenario: Captured guest foreground navigates its owning group
- **WHEN** a captured guest is the foreground window and its current group and live container are proven
- **THEN** the corresponding container performs shared tab navigation

#### Scenario: Container chrome foreground navigates its group
- **WHEN** the owning TabDock container/chrome is foreground and live
- **THEN** the corresponding container performs shared tab navigation

#### Scenario: Unrelated foreground is a strict no-op
- **WHEN** an unrelated desktop application is foreground
- **THEN** the successful hotkey registration consumes no application navigation and no TabDock group changes

#### Scenario: Recycled or stale HWND is a strict no-op
- **WHEN** the foreground HWND no longer proves the captured identity or the owning container has closed/recycled
- **THEN** no navigation or native presentation mutation occurs

#### Scenario: Chrome/modal interaction defers navigation
- **WHEN** a TabDock context menu, capture panel, rename editor, or close prompt is active
- **THEN** global navigation does not re-enter or disturb that interaction

### Requirement: Registration failure SHALL preserve safe fallback behavior

If either global navigation hotkey cannot be registered, TabDock SHALL remain
functional, expose the availability diagnostically and where useful in UI
help/tooltip text, and retain local Ctrl+Tab navigation.

#### Scenario: Navigation registration fails
- **WHEN** one or both PageUp/PageDown registrations fail
- **THEN** startup continues, the local shortcuts remain available, and no failed registration is treated as a working global shortcut

#### Scenario: Repeated key input is suppressed
- **WHEN** a user holds a registered global navigation combination
- **THEN** the registration uses the Windows no-repeat modifier so only the intended hotkey notification is handled per press

### Requirement: Global and local paths SHALL share one navigation operation

The global and local navigation entry points SHALL invoke one container-level
navigation operation backed by the canonical navigation policy. They SHALL
preserve one-tab no-op behavior, wraparound, identity/reference targeting, and
split presentation semantics.

#### Scenario: One-tab group remains unchanged
- **WHEN** either local or scoped global navigation is requested for a one-tab group
- **THEN** no tab or presentation state changes
