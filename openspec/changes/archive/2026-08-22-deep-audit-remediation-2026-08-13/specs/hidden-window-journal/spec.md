## ADDED Requirements

### Requirement: The recovery journal SHALL cover the entire capture session
The versioned journal SHALL contain every captured guest from before the first
TabDock presentation mutation until a release is durably complete. It SHALL
store enough original placement, show-state, DWM-transition, and stable identity
information to restore the guest after abrupt termination without touching a
recycled HWND.

#### Scenario: An active guest is rescued after hard kill
- **WHEN** TabDock is terminated while an active guest is positioned over a container
- **THEN** the next startup validates HWND/PID/executable/class/process-start identity and restores the guest's original placement, maximized/minimized/normal state, visibility, and DWM transition setting

#### Scenario: An intentionally self-hidden guest is not resurrected
- **WHEN** a guest initiates a tray-style hide
- **THEN** a durable no-rescue transition is committed before the hide and rescue does not show the guest

### Requirement: Rescue SHALL not make unrelated z-order changes
Recovery SHALL restore placement and visibility without attempting to reconstruct
unknown unrelated desktop z-order. Recycled or insufficiently identified HWNDs
SHALL be discarded without native mutation.

#### Scenario: A recycled HWND is ignored
- **WHEN** a journal entry's HWND now belongs to another PID, executable, class, or known process start
- **THEN** rescue performs no ShowWindow, placement, or DWM mutation on that HWND
