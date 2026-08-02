## MODIFIED Requirements

### Requirement: At most one pending re-assert timer per ContainerWindow
`ContainerWindow`'s `WM_ACTIVATE` handler (`Views/ContainerWindow.xaml.cs:142-164`) SHALL keep a single cancellable `DispatcherTimer` field for its 120ms guest re-assert delay. A new activation event that arrives while a prior re-assert timer is still pending SHALL stop and replace that prior timer rather than allocating an additional, independently-running one.

The re-assert timer's tick SHALL only call `BringToFront` when the container is not being used for its own chrome interaction. It SHALL be suppressed when any of the container's interactive chrome is currently active: the tab context menu (Pop out / Close window) is open, the accent-color context menu is open, or the group rename box is being edited. Suppressing the re-assert leaves the guest positioned exactly where it was — the suppression only prevents the foreground steal that would otherwise close the open chrome UI — so the guest must remain correctly placed and visible during suppression.

#### Scenario: Rapid repeated activation does not queue multiple timers
- **WHEN** `WM_ACTIVATE` fires two or more times within 120ms of each other while a shepherded guest is active
- **THEN** only the most recently scheduled re-assert timer is allowed to fire; any earlier pending timer for the same burst is stopped before it fires

#### Scenario: Re-assert behavior is unchanged for a single activation
- **WHEN** `WM_ACTIVATE` fires once with `WA_ACTIVE`/`WA_CLICKACTIVE`, a shepherded guest is active, and no container chrome UI is open
- **THEN** the guest's position/z-order is re-asserted via `BringToFront` after the same 120ms delay and the same visibility re-check as before this change, with no observable behavior difference in the single-activation case

#### Scenario: Tab context menu stays open when the container is activated by the right-click
- **WHEN** a shepherded guest holds the system foreground and the user right-clicks a tab, activating the container and opening the tab context menu
- **THEN** the deferred re-assert timer fires without calling `BringToFront`, and the context menu remains open and clickable

#### Scenario: Accent-color menu stays open when the container is activated by the chip click
- **WHEN** a shepherded guest holds the system foreground and the user clicks the accent-color chip, activating the container and opening the color context menu
- **THEN** the deferred re-assert timer fires without calling `BringToFront`, and the color menu remains open

#### Scenario: Rename box keeps focus when the container is activated by the title double-click
- **WHEN** a shepherded guest holds the system foreground and the user double-clicks the group title, activating the container and entering rename mode
- **THEN** the deferred re-assert timer fires without calling `BringToFront`, and the rename box retains keyboard focus

#### Scenario: Chrome-interaction suppression leaves the guest correctly placed
- **WHEN** the re-assert is suppressed because container chrome UI is open
- **THEN** the guest remains visible and positioned over the container's content area for the whole time the chrome UI is open
