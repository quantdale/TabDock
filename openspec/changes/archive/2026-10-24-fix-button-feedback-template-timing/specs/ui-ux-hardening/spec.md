## ADDED Requirements

### Requirement: Button feedback SHALL bind to realized current-template parts

A feedback-enabled shared or derived button template SHALL display its configured hover/pressed feedback once its current template is realized. Early attachment before child creation SHALL NOT permanently disable feedback by retaining missing-part results. Reload or template replacement SHALL retire obsolete part references and attach at most one current feedback response. Intentionally missing optional parts SHALL degrade without exceptions, endless retries or changes to command/keyboard-focus behavior.

#### Scenario: First loaded hover uses the real overlay

- **WHEN** a button is constructed, its actual template is realized and loaded, and pointer-enter state is driven
- **THEN** the current hover overlay reaches its configured active state and returns on pointer leave, including when initial attachment preceded child creation

#### Scenario: Pressed and keyboard feedback use the current template

- **WHEN** pointer or native keyboard activation changes pressed state after realization
- **THEN** the configured pressed feedback and release state appear without duplicate commands or lost keyboard focus indication

#### Scenario: Template replacement invalidates queued feedback

- **WHEN** a button template changes while part resolution or feedback work is queued
- **THEN** only the new template receives later feedback and obsolete parts can be collected

#### Scenario: Missing optional parts remain benign

- **WHEN** a button variant omits an optional overlay or is unloaded before part resolution
- **THEN** it remains activatable, performs no endless retry loop, and has no unhandled exception or stale-template update
