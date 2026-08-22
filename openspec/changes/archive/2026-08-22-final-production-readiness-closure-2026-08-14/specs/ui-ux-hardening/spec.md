## MODIFIED Requirements

### Requirement: Deferred split positioning respects HDWP generation boundaries
The split positioning batch SHALL validate each guest's cheap capture
generation immediately before its `DeferWindowPos` queue operation. A failed
native `DeferWindowPos` SHALL be abandoned without `EndDeferWindowPos` as
required by Win32. If a later generation check fails while a valid HDWP exists,
the valid batch SHALL be closed with `EndDeferWindowPos` and the stale-guest
fallback SHALL not run. The final check-to-commit interval between the last
identity check and the native compositor commit is an unavoidable bounded
Win32 race; the product SHALL document it as a residual limitation and SHALL
not claim atomic identity proof across that interval.

#### Scenario: A stale split guest is not queued
- **WHEN** a split guest generation changes before its deferred queue operation
- **THEN** that guest is not passed to `DeferWindowPos`, the valid HDWP lifecycle is closed safely, and no fallback mutation targets the stale guest

#### Scenario: The residual check-to-commit race is accurately bounded
- **WHEN** a target changes after its final pre-queue validation but before Windows commits the valid HDWP
- **THEN** TabDock relies on the documented Win32 batch contract, does not claim impossible atomic cancellation, and preserves the existing no-reparent/fail-closed architecture
