## ADDED Requirements

### Requirement: At most one pending re-assert timer per ContainerWindow
`ContainerWindow`'s `WM_ACTIVATE` handler (`Views/ContainerWindow.xaml.cs:142-164`) SHALL keep a single cancellable `DispatcherTimer` field for its 120ms guest re-assert delay. A new activation event that arrives while a prior re-assert timer is still pending SHALL stop and replace that prior timer rather than allocating an additional, independently-running one.

#### Scenario: Rapid repeated activation does not queue multiple timers
- **WHEN** `WM_ACTIVATE` fires two or more times within 120ms of each other while a shepherded guest is active
- **THEN** only the most recently scheduled re-assert timer is allowed to fire; any earlier pending timer for the same burst is stopped before it fires

#### Scenario: Re-assert behavior is unchanged for a single activation
- **WHEN** `WM_ACTIVATE` fires once with `WA_ACTIVE`/`WA_CLICKACTIVE` and a shepherded guest is active
- **THEN** the guest's position/z-order is re-asserted via `BringToFront` after the same 120ms delay and the same visibility re-check as before this change, with no observable behavior difference in the single-activation case

### Requirement: At most one pending state-settled snapshot timer per ContainerWindow
`ContainerWindow_StateChanged`'s diagnostic "settled" snapshot timer (`Views/ContainerWindow.xaml.cs:296-320`) SHALL keep a single cancellable `DispatcherTimer` field, independent of the re-assert timer above. A new state-change event that arrives while a prior settle timer is still pending SHALL stop and replace that prior timer.

#### Scenario: Rapid maximize/restore toggling logs at most one settled snapshot per burst
- **WHEN** the window's `WindowState` changes two or more times within 750ms of each other
- **THEN** only one "settled" diagnostic log snapshot is produced for that burst, corresponding to the state after the last change, rather than one per intermediate state change
