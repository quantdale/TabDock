## ADDED Requirements

### Requirement: Deferred window positioning SHALL chain HDWP handles
The native `DeferWindowPos` declaration SHALL return the updated HDWP handle.
Every successful call SHALL pass its returned handle to the next call and the
final handle to `EndDeferWindowPos`. If any call fails, the batch SHALL be
abandoned without calling `EndDeferWindowPos`, and the caller SHALL use its
defined safe fallback.

#### Scenario: A changed HDWP is passed through a three-window split batch
- **WHEN** a deterministic native seam returns a different nonzero handle from each `DeferWindowPos` call
- **THEN** each next call receives the immediately preceding handle and End receives the final handle

#### Scenario: A failed middle defer abandons the batch
- **WHEN** a middle `DeferWindowPos` returns zero
- **THEN** no later defer or End call occurs and the caller can execute the existing safe fallback

### Requirement: Container maximize bounds SHALL follow the containing monitor
The container SHALL use the work area of the monitor containing its HWND for
native maximize position and size. A primary-monitor-only WPF maximum SHALL NOT
clamp a secondary monitor's larger work area before the native result applies.

#### Scenario: A larger secondary monitor receives a full work-area maximize
- **WHEN** a container is moved to a larger secondary monitor and maximized
- **THEN** its outer rectangle matches that monitor's work area without taskbar overlap

### Requirement: Hung-guest min-track probing SHALL be bounded and cached
Native min-track probing SHALL use a documented short `SMTO_ABORTIFHUNG` bound,
run only during dirty constraint refreshes, and retain the last successful value
for the same captured identity when a later probe times out or fails.

#### Scenario: A non-pumping guest does not cause a long split freeze
- **WHEN** one or both visible guests do not pump `WM_GETMINMAXINFO`
- **THEN** the dispatcher wait is bounded per guest, the prior identity-scoped constraint remains if available, and layout continues without a resize war
