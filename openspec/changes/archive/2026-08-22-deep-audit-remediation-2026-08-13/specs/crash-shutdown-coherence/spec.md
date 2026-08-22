## ADDED Requirements

### Requirement: Session-ending teardown SHALL be one-way and idempotent
Once `Application_SessionEnding` begins guest release, TabDock SHALL normalize
its model/container state, stop monitoring, and deliberately call `Shutdown`.
It SHALL NOT attempt to resume as an operational app if another process cancels
the Windows logoff/shutdown sequence.

#### Scenario: Session teardown cannot leave a hookless half-running app
- **WHEN** session-ending teardown has released and normalized captured guests
- **THEN** TabDock exits through its normal `Application_Exit` path and repeated exit cleanup is harmless
