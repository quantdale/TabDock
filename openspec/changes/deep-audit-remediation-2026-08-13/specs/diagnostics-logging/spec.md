## ADDED Requirements

### Requirement: Storage failure SHALL degrade without weakening capture safety
If AppData logging or persistence storage is unavailable, TabDock MAY continue
with bounded in-memory diagnostics and disabled persistence, but it SHALL show a
clear warning and SHALL refuse capture unless the durable guest recovery journal
can be written before mutation.

#### Scenario: AppData is unavailable at startup
- **WHEN** log/state/journal directory creation or probe fails
- **THEN** the app remains launchable in degraded mode, explains the limitation, and does not hide or capture a guest without durable recovery
