## ADDED Requirements

### Requirement: Live logical export SHALL use immutable owner-thread observations

An in-process diagnostic export SHALL collect logical presentation state on its owning UI thread before background report/archive work. Once captured, that logical data SHALL be immutable with respect to subsequent live-window changes, and background work SHALL NOT read live controls or view models. Every available container observation SHALL be included; partial/unavailable collection SHALL be distinguished from a genuinely empty session with sanitized status in the same bundle. Collection SHALL remain side-effect-free.

#### Scenario: One live container produces one logical entry

- **WHEN** an in-process export has one available dispatcher-owned container
- **THEN** its bundle includes that container's group, active/split state and expected geometry without a cross-thread access failure or silently empty logical array

#### Scenario: A container disappears during collection

- **WHEN** a container cannot be observed during capture
- **THEN** other available observations remain included and the same bundle records sanitized partial/unavailable evidence rather than claiming an empty healthy session

#### Scenario: Live state changes after capture

- **WHEN** the captured container changes selection or closes while the archive is written
- **THEN** the archive contains the captured immutable logical values and never rereads the disposed/live UI object

### Requirement: Hotkey diagnostic export SHALL not block its UI dispatcher

The hotkey export SHALL perform only bounded logical collection on the UI dispatcher, then observe asynchronous report/archive completion or failure without synchronously waiting on worker I/O or blocking dispatcher round trips. UI-free CLI export without a live provider SHALL remain supported and SHALL identify the absence of in-process logical evidence honestly. Existing privacy redaction and no-native-mutation guarantees SHALL remain unchanged.

#### Scenario: Archive work is held while UI remains usable

- **WHEN** archive work is deliberately held on a test gate after logical capture
- **THEN** the hotkey operation yields to the UI dispatcher before that gate opens and eventual success/failure is observed

#### Scenario: CLI export has no live provider

- **WHEN** export runs as a diagnostic-only CLI process
- **THEN** it emits a valid sanitized report with explicit no-live-provider logical status without creating the main UI or invoking native presentation mutation
