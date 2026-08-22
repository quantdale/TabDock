# diagnostics-logging

## Purpose
Captures the bounded-fallback, rotation-backoff, and thread-safe-disposal semantics of `LoggingService`'s failure paths.
## Requirements
### Requirement: The logging fallback file is bounded
When the primary log file is unwritable, `LoggingService`'s `TabDock.log.err` fallback SHALL NOT grow without bound: it SHALL either be capped (retaining at most the most recent N KB) or suppress repeated identical errors.

#### Scenario: A persistent logging failure does not fill the disk
- **WHEN** the primary log file remains unwritable for an entire long-running session
- **THEN** the `.err` file's size stays below a fixed bound regardless of how many log lines were attempted

### Requirement: A failed log rotation backs off instead of churning
When a rotation `File.Move` fails (e.g. the file is held open by another tool), `LoggingService` SHALL retry rotation at most once per N batches rather than performing a close/delete/move/open cycle on every batch for the rest of the session.

#### Scenario: One transient rotation failure costs one retry burst, not permanent churn
- **WHEN** a rotation attempt fails while logging otherwise continues normally
- **THEN** logging continues uninterrupted, and rotation is re-attempted on a bounded cadence instead of every batch

### Requirement: LoggingService.Dispose is safe to call concurrently
`LoggingService.Dispose` SHALL be idempotent across threads (e.g. via `Interlocked.Exchange` on an int flag): concurrent callers SHALL NOT both reach `CompleteAdding()` or `Join`, and the second call SHALL return without throwing.

#### Scenario: A crash path racing normal shutdown does not throw from Dispose
- **WHEN** `Dispose` is invoked from two threads concurrently (normal exit and a crash path)
- **THEN** exactly one caller performs the teardown, the other returns cleanly, and no `InvalidOperationException` escapes

### Requirement: Supervised recovery titles are terminal-safe
The local candidate list used by supervised pending recovery SHALL normalize
untrusted external window titles to one bounded line. C0/C1 controls, ESC,
DEL, and Unicode line/paragraph separators SHALL be removed or replaced before
the title reaches the interactive writer. This local display contract SHALL
not weaken the existing title hashing/redaction contract for doctor reports or
support bundles.

#### Scenario: Control-bearing title cannot alter the terminal
- **WHEN** a candidate title contains ANSI/OSC controls, C0/C1 characters,
  line separators, and ordinary Unicode
- **THEN** no raw terminal control or line break is emitted, the title remains
  bounded, and ordinary Unicode remains readable

### Requirement: Storage failure SHALL degrade without weakening capture safety
If AppData logging or persistence storage is unavailable, TabDock MAY continue
with bounded in-memory diagnostics and disabled persistence, but it SHALL show a
clear warning and SHALL refuse capture unless the durable guest recovery journal
can be written before mutation.

#### Scenario: AppData is unavailable at startup
- **WHEN** log/state/journal directory creation or probe fails
- **THEN** the app remains launchable in degraded mode, explains the limitation, and does not hide or capture a guest without durable recovery

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
