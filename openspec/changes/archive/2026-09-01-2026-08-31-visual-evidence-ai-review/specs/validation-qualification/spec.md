# validation-qualification delta — visual evidence and AI-assisted presentation review

## MODIFIED Requirements

### Requirement: CI SHALL run hermetic behavioral qualification

CI SHALL run Release builds for all committed projects, geometry and diagnostic
self-tests, persistence/native/privacy fixtures, OpenSpec validation, version
and doctor smoke, support-bundle inspection, publish smoke, the explicit
dependency-audit policy, deterministic catalog and virtual-topology laboratory
suites, and offline qualification-bundle verification. Unsafe real-input and
hardware tests SHALL remain supervised and separate. CI SHALL retain the
machine-readable qualification artifacts with the exact candidate identity and
schema generation.

When visual-evidence support is present, ordinary CI SHALL additionally test
visual artifact encoding/schema/path/hash logic, deterministic synthetic image
fixtures, visual-review packet construction, visual-review-result verification,
tamper/stale/missing negative cases, and compatibility with historical
non-visual evidence. Ordinary CI SHALL NOT require a real interactive desktop,
unrestricted screen capture, or a live multimodal model to pass.

Synthetic visual fixtures and pre-authored structured review-result fixtures
MAY be retained as bounded CI evidence. They SHALL be labeled synthetic and
SHALL NOT be promoted to physical visual certification of TabDock.

#### Scenario: A hermetic regression fails CI

- **WHEN** a self-test, catalog contract, topology assertion, bundle/privacy
  check, visual-evidence schema/hash/path check, visual-review verifier check,
  OpenSpec validation, or Release/publish check fails
- **THEN** the CI job exits nonzero and does not convert the failure into a
  warning-only result

#### Scenario: Synthetic topology is covered in CI

- **WHEN** the topology laboratory passes its deterministic matrix
- **THEN** CI records the result as synthetic coverage and does not mark
  physical mixed-DPI qualification as complete

#### Scenario: Synthetic visual-review fixtures pass CI

- **WHEN** deterministic in-memory/test-owned images produce valid visual
  manifests/review packets and known structured review-result fixtures verify
- **THEN** CI records visual infrastructure/schema coverage only and does not
  claim that a multimodal model inspected a live physical TabDock run

#### Scenario: CI host has no interactive desktop or vision model

- **WHEN** the CI environment cannot safely capture a physical Windows desktop
  or invoke a multimodal reviewer
- **THEN** the hermetic gate uses synthetic image fixtures and verifier tests
  and does not weaken or skip the deterministic visual infrastructure contract

### Requirement: ValidationDriver SHALL support bounded configurable shards

The driver SHALL discover or accept explicit TabDock and GuineaPig paths for
Debug/Release and RID variants. Named shards SHALL be declared by the canonical
scenario catalog, SHALL contain only their catalog members, and SHALL have
explicit count/runtime budgets. Every scenario SHALL declare capability
requirements, use the canonical qualification outcome vocabulary, and emit a
result artifact linked from the run manifest. A direct scenario or shard run
SHALL write a versioned child manifest in an isolated artifact directory.
`all` SHALL create a parent run identity, import and verify every declared
child manifest, preserve per-process spawn/time safety caps, and fail closed on
missing, malformed, contradictory, stale, or tampered child evidence.

A scenario MAY additionally declare a bounded visual-evidence level and whether
multimodal visual review is required for that scenario/gate. When visual
evidence is enabled, the child attempt SHALL index its visual manifest and
retained image hashes. When a visual review packet/result exists, the run
hierarchy SHALL bind and verify it by packet/image hash, candidate, run,
scenario and attempt identity.

Visual-review verdicts (`VISUAL_OK`, `VISUAL_SUSPECT`,
`VISUAL_DEFECT`, `REVIEW_UNAVAILABLE`) SHALL remain a separate evidence
dimension from the canonical scenario outcome vocabulary. A visual verdict
SHALL NOT override physical lease, identity, foreground, ownership, cleanup, or
native assertion outcomes. A gate that explicitly requires visual review SHALL
remain non-pass when required image/review evidence is missing, invalid,
tampered, suspect/defective, or unavailable according to its declared policy.

Historical scenarios/runs that do not declare visual evidence SHALL remain
valid under their existing contracts and SHALL not be required to fabricate
visual artifacts.

#### Scenario: Synthetic pass does not satisfy a physical field cell

- **WHEN** a headless or synthetic presentation-integrity scenario passes but the matching requirement calls for real guarded input/topology
- **THEN** deterministic coverage is recorded separately and the physical cell remains pending or blocked until physically qualified

#### Scenario: Release artifacts run without source edits

- **WHEN** the driver is invoked with `--configuration Release` against Release
  artifacts
- **THEN** it locates both executables, validates their candidate identities,
  resolves declared capabilities before destructive setup, and runs the selected
  catalog scenario or shard

#### Scenario: All runs as bounded shards

- **WHEN** the driver is invoked with `--yes all`
- **THEN** it runs the catalog-declared shards sequentially in isolated child
  directories, verifies each child manifest and exit outcome, and reports the
  first disagreement or non-pass shard without a monolithic impossible budget

#### Scenario: A scenario cannot prove its harness boundary

- **WHEN** a selector, cleanup, ownership, or evidence invariant is not proven
- **THEN** the scenario is recorded as `FAIL_HARNESS` with bounded evidence and
  the shard does not relabel it as a product failure

#### Scenario: Reruns remain first-attempt authoritative

- **WHEN** a scenario is run with investigation reruns
- **THEN** the first attempt remains authoritative and a later pass after a
  valid failure is recorded as `FLAKE_UNCLASSIFIED`, never as PASS

#### Scenario: A visual-enabled attempt retains screenshots

- **WHEN** a catalog scenario enables visual checkpoints and the recorder
  successfully captures them
- **THEN** its child evidence links a versioned visual manifest and immutable
  image hashes under the attempt artifact root without changing the scenario's
  native outcome semantics by itself

#### Scenario: A required visual artifact is missing or tampered

- **WHEN** a scenario/gate declares a checkpoint required but its PNG, visual
  manifest, hash, candidate binding, or relative path fails verification
- **THEN** the attempt is `FAIL_HARNESS` and parent/shard aggregation cannot
  infer visual or release PASS

#### Scenario: A visual-review-required gate has no capable reviewer

- **WHEN** all physical/native scenario checks pass but the catalog/gate requires
  multimodal review and the reviewer reports `REVIEW_UNAVAILABLE`
- **THEN** the gate remains non-pass with the missing review capability named;
  the native checks remain recorded independently

#### Scenario: AI review says OK while native qualification failed

- **WHEN** a valid hash-bound review says `VISUAL_OK` but the underlying
  attempt lost its lease, identity, foreground, ownership, cleanup, or another
  canonical prerequisite
- **THEN** the native failure/block remains authoritative and the visual review
  cannot promote the attempt

#### Scenario: AI review identifies a visible defect while simple metrics pass

- **WHEN** valid reviewed screen-composited imagery is
  `VISUAL_DEFECT`/`VISUAL_SUSPECT` even though geometry, brightness, or
  other simple pixel metrics appear nominal
- **THEN** the visual finding remains in the evidence and a gate requiring
  visual review cannot pass until the finding is dispositioned through normal
  product/harness investigation
