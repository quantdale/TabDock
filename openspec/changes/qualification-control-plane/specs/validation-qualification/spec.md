## MODIFIED Requirements

### Requirement: CI SHALL run hermetic behavioral qualification
CI SHALL run Release builds for all committed projects, geometry and diagnostic self-tests, persistence/native/privacy fixtures, OpenSpec validation, version and doctor smoke, support-bundle inspection, publish smoke, the explicit dependency-audit policy, deterministic catalog and virtual-topology laboratory suites, and offline qualification-bundle verification. Unsafe real-input and hardware tests SHALL remain supervised and separate. CI SHALL retain the machine-readable qualification artifacts with the exact candidate identity and schema generation.

#### Scenario: A hermetic regression fails CI
- **WHEN** a self-test, catalog contract, topology assertion, bundle/privacy check, OpenSpec validation, or Release/publish check fails
- **THEN** the CI job exits nonzero and does not convert the failure into a warning-only result

#### Scenario: Synthetic topology is covered in CI
- **WHEN** the topology laboratory passes its deterministic matrix
- **THEN** CI records the result as synthetic coverage and does not mark physical mixed-DPI qualification as complete

### Requirement: ValidationDriver SHALL support bounded configurable shards
The driver SHALL discover or accept explicit TabDock and GuineaPig paths for Debug/Release and RID variants. Named shards SHALL be declared by the canonical scenario catalog, SHALL contain only their catalog members, and SHALL have explicit count/runtime budgets. A direct scenario or shard run SHALL write a versioned child manifest in an isolated artifact directory. `all` SHALL create a parent run identity, import and verify every declared child manifest, preserve per-process spawn/time safety caps, and fail closed on missing, malformed, contradictory, stale, or tampered child evidence.

#### Scenario: Release artifacts run without source edits
- **WHEN** the driver is invoked with `--configuration Release` against Release artifacts
- **THEN** it locates both executables, validates their candidate identities, and runs the selected catalog scenario or shard

#### Scenario: All runs as verified bounded shards
- **WHEN** the driver is invoked with `--yes all`
- **THEN** it runs the catalog-declared shards sequentially in isolated child directories, verifies each child manifest and exit outcome, and reports the first disagreement or non-pass shard without a monolithic impossible budget

#### Scenario: Reruns remain first-attempt authoritative
- **WHEN** a scenario is run with investigation reruns
- **THEN** the first attempt remains authoritative and a later pass after a valid failure is recorded as `FLAKE_UNCLASSIFIED`, never as PASS
