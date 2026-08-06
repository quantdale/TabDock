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
