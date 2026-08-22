## ADDED Requirements

### Requirement: Desktop reorder callbacks fail closed before dispatch work

For a desktop-source `EVENT_OBJECT_REORDER`, the monitor SHALL snapshot the
foreground HWND and resolve its current captured member before allocating the
normal diagnostic record or posting to the UI dispatcher. If no captured
member resolves, the callback SHALL return without posting or recording the
normal reorder event. If a member resolves, the monitor SHALL preserve the
captured `CapturedWindow` reference, diagnostic callback/dispatch events, UI
dispatcher hop, and dispatch-time reference identity validation.

#### Scenario: Uncaptured desktop reorder is dropped early

- **WHEN** a desktop reorder callback observes an uncaptured foreground HWND
- **THEN** it performs no normal reorder post or diagnostic allocation

#### Scenario: Captured desktop reorder remains observable

- **WHEN** a desktop reorder callback observes a captured foreground HWND
- **THEN** it records the relevant callback event and posts exactly one event
  for UI-thread dispatch

#### Scenario: Released or recycled member is rejected at dispatch

- **WHEN** a captured reorder is queued and its member is released or the same
  numeric HWND resolves to a different `CapturedWindow` before dispatch
- **THEN** the queued event performs no lifecycle/z-order action

### Requirement: Diagnostic event metadata is copied once

`DiagnosticTrace.Record` SHALL expose a non-null `Data` dictionary while
creating exactly one empty dictionary for a no-metadata event or one defensive
copy for supplied metadata. Mutating caller metadata after `Record`, or a
mutable dictionary returned by `Snapshot`, SHALL NOT mutate stored history.

#### Scenario: Defensive metadata remains isolated

- **WHEN** a caller records an event with a metadata dictionary and later
  mutates either the caller dictionary or a dictionary returned by `Snapshot`
- **THEN** the recorded event retains the original values and its public
  `Data` dictionary remains non-null
