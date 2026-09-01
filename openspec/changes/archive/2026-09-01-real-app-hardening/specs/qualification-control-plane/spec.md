# qualification-control-plane

## ADDED Requirements

### Requirement: Real-app planning SHALL be defensive and capability-derived

The driver SHALL provide a no-input planning mode that explains real-app catalog scenarios required for the gate and classifies each as runnable/blocked/skipped/optional from the privacy-safe application/topology snapshot. Capability discovery SHALL run before state isolation, process launch, or input. Every scenario SHALL declare the `RequiresInteractiveSession`, `InputRequirement`, `RequiresSupervision`, `DestructiveState`, `GuestFamily`, and `RequiredApplications`/`RequiredBrowsers` that the catalog already carries; `chrome-normal`/`edge-normal`/`brave-normal`/`notepad-broker`/`wt` SHALL be the only real-app families, and an unregistered real app SHALL fail closed as `FAIL_HARNESS`.

#### Scenario: Real-app planning runs without input
- **WHEN** `plan realApp` or `plan all` is requested
- **THEN** it emits policy/capability/lease blocks without starting TabDock, sending input, capturing, or invoking a reviewer

### Requirement: Real-app qualification manifests SHALL be verifiable

Child/shard manifests for real-app cells SHALL carry candidate/executable/driver SHA, catalog generation, HWND/process-start identity, run-owned/adopted, lease/topology, artifactIndex paths/hashes, and visual-privacy facts. Parent aggregation SHALL verify shard identity, outcome, artifact existence, and first-attempt lineage; a missing or tampered child SHALL be `FAIL_HARNESS`.

#### Scenario: Real-app child manifest is stale
- **WHEN** a `browser-fullscreen-contained` child manifest names a different candidate or its packet hash differs from bytes on disk
- **THEN** parent/import verification rejects it and the release gate cannot claim `PASS`
