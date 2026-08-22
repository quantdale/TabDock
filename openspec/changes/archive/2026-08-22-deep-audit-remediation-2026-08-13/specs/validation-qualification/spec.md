## ADDED Requirements

### Requirement: CI SHALL run hermetic behavioral qualification
CI SHALL run Release builds for all committed projects, geometry and diagnostic
self-tests, persistence/native/privacy fixtures, OpenSpec validation, version
and doctor smoke, support-bundle inspection, publish smoke, and the explicit
dependency-audit policy. Unsafe real-input and hardware tests SHALL remain
supervised and separate.

#### Scenario: A hermetic regression fails CI
- **WHEN** a self-test, fixture, bundle privacy check, OpenSpec validation, or Release/publish check fails
- **THEN** the CI job exits nonzero and does not convert the failure into a warning-only result

### Requirement: ValidationDriver SHALL support bounded configurable shards
The driver SHALL discover or accept explicit TabDock and GuineaPig paths for
Debug/Release and RID variants. Named shards SHALL contain known scenarios, and
`all` SHALL orchestrate bounded shard processes without removing per-process
spawn/time safety caps.

#### Scenario: Release artifacts run without source edits
- **WHEN** the driver is invoked with `--configuration Release` against Release artifacts
- **THEN** it locates both executables and runs the selected shard

#### Scenario: All runs as bounded shards
- **WHEN** the driver is invoked with `--yes all`
- **THEN** it runs the named shards sequentially, each with independent safety budgets, and reports the first failing shard without a monolithic impossible budget
