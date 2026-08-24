## Purpose

Provides one typed, portable, fail-closed evidence control plane for ValidationDriver qualification and release-candidate decisions, from scenario planning through offline verification and independent-machine import.

## ADDED Requirements

### Requirement: Scenario qualification SHALL have one canonical catalog
Every dispatchable ValidationDriver scenario SHALL be represented exactly once in a versioned catalog entry containing its stable ID, dispatch identifier, shard, execution class, guest family, required applications or browsers, interactive/input requirements, topology requirements, supervision and destructive-state classification, expected runtime budget, default inclusion policy, and release-evidence eligibility. CLI listing and validation, capability preflight, shard projection, documentation contracts, and orchestration SHALL be derived from that catalog.

#### Scenario: An unregistered dispatch handler fails closed
- **WHEN** a dispatchable scenario has no catalog entry or a catalog entry has no resolvable handler
- **THEN** startup validation returns `FAIL_HARNESS` and the scenario cannot run or contribute evidence

#### Scenario: A scenario cannot belong to incompatible shards
- **WHEN** catalog validation finds duplicate IDs, incompatible shard assignments, an unknown shard, or a shard over its declared runtime/count budget
- **THEN** catalog validation fails before any TabDock or guest process is launched

### Requirement: Qualification manifests SHALL form a verified hierarchy
A direct scenario or shard run SHALL emit a versioned child manifest in an isolated artifact directory. An `all` invocation SHALL create a distinct parent run identity and manifest that imports every attempted child manifest, verifies schema, run kind, candidate commit and executable hashes, driver identity, shard identity, timestamps, outcomes, artifact existence, catalog generation, and exact scenario membership. The parent SHALL record aggregate outcome counts, capability observations, first-attempt-authoritative rerun lineage, child-manifest hashes, and relative links to result, JUnit, and timeline artifacts.

#### Scenario: A child exit code disagrees with its manifest
- **WHEN** a child process exits with a result that disagrees with its verified child manifest
- **THEN** the parent records `FAIL_HARNESS` for that shard and the parent run cannot be release PASS

#### Scenario: A partial or tampered `all` run is imported
- **WHEN** a child manifest is missing, malformed, stale, duplicated, tampered, assigned to the wrong shard, bound to another candidate, or absent after timeout/cancellation
- **THEN** the parent records `FAIL_HARNESS` and never infers success from process completion alone

#### Scenario: A valid full hierarchy is aggregated
- **WHEN** every declared child manifest and linked artifact passes verification
- **THEN** the parent contains every shard and scenario/attempt exactly once, preserves blocked/skipped/flaky outcomes, and derives its aggregate counts from imported evidence

### Requirement: Qualification bundles SHALL be portable and offline-verifiable
A qualification bundle SHALL bind a source commit, semantic version, exact candidate executable hash, driver hash, catalog/schema generations, run-manifest hashes, timestamps, privacy-safe OS/environment classification, capability observations, outcome counts, and an artifact index of normalized relative paths. An offline verifier SHALL validate all hashes, paths, required artifacts, schema versions, candidate/source consistency, outcome summaries, and bundle invariants without launching TabDock or trusting console output.

#### Scenario: Evidence bytes are modified after capture
- **WHEN** a referenced manifest, result, JUnit file, timeline, or driver artifact is changed or removed
- **THEN** offline verification fails and identifies the relative artifact and violated hash/existence contract

#### Scenario: An unsafe bundle path is supplied
- **WHEN** an artifact index contains an absolute path, traversal segment, duplicate path, or path that resolves outside the bundle root
- **THEN** offline verification fails before reading or accepting the artifact as qualification evidence

#### Scenario: An unsupported bundle schema is supplied
- **WHEN** the bundle uses a future schema or a schema outside the explicitly accepted migration window
- **THEN** verification returns a deterministic unsupported-schema failure rather than partially accepting the bundle

### Requirement: Qualification planning and remote evidence import SHALL be defensive
The driver SHALL provide a no-input planning mode that explains catalog scenarios required for a requested gate and classifies each as runnable, blocked, skipped, or optional from a privacy-safe capability snapshot. Independent-machine reports SHALL be structured, candidate-bound, hash-verifiable, and importable without executing returned scripts or binaries; imported evidence SHALL retain its machine identity and synthetic/physical classification.

#### Scenario: A second machine returns an untrusted report
- **WHEN** an imported report contains malformed fields, a candidate/source/hash mismatch, an unsupported OS classification, a future timestamp, or executable/script content
- **THEN** import fails closed and no external release gate is changed

#### Scenario: Planning runs on an unsafe desktop
- **WHEN** planning is requested without sending input
- **THEN** it emits a machine-readable plan with capability blocks and never starts TabDock, a guest, or a physical scenario
