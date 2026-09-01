# validation-qualification delta — visual closure and performance re-baseline

## MODIFIED Requirements

### Requirement: CI SHALL run hermetic behavioral qualification

CI SHALL run Release builds for all committed projects, geometry and diagnostic
self-tests, persistence/native/privacy fixtures, OpenSpec validation, version
and doctor smoke, support-bundle inspection, publish smoke, the explicit
dependency-audit policy, deterministic catalog and virtual-topology laboratory
suites, offline qualification-bundle verification, and visual evidence
contract/performance regression checks when visual infrastructure is present.
Unsafe real-input, hardware, and multimodal review tests SHALL remain
supervised and separate. CI SHALL retain machine-readable artifacts with the
exact candidate identity and schema generation.

Visual CI checks SHALL cover synthetic/in-memory PNGs, strict collection
validation, packet/result hash binding, contact-sheet derived-failure
semantics, stale/tampered/missing/path cases, disabled-mode zero-work
invariants, bounded ring/artifact budgets, cleanup/cancellation, and
historical non-visual compatibility. They SHALL NOT require an interactive
desktop, unrestricted screen capture, a live model, network access, or
provider credentials.

#### Scenario: A hermetic visual regression fails CI

- **WHEN** a visual schema/hash/path, disabled-work, bounded-resource,
  compatibility, OpenSpec, build, test, or Release/publish check fails
- **THEN** CI exits nonzero and does not convert the failure into a warning-only
  result

#### Scenario: Visual evidence is disabled in CI

- **WHEN** the ordinary CI/resource path runs with visual evidence `none`
- **THEN** capture requests, encodes, retained bytes, packet/contact work,
  visual artifacts, workers, and timers are zero; any policy branch overhead is
  separately measured and no physical screenshot is captured

#### Scenario: Synthetic visual checks pass

- **WHEN** deterministic fixtures and structured review-result fixtures pass
- **THEN** CI records synthetic infrastructure coverage only and does not claim
  multimodal inspection or physical visual certification

#### Scenario: CI lacks a desktop or vision capability

- **WHEN** the CI host cannot safely capture a desktop or invoke a reviewer
- **THEN** it uses synthetic fixtures and retains the supervised visual gate as
  pending/blocked rather than weakening the contract

 
#### Scenario: A hermetic regression fails CI

- **WHEN** a self-test, catalog contract, topology assertion, bundle/privacy
  check, OpenSpec validation, or Release/publish check fails
- **THEN** the CI job exits nonzero and does not convert the failure into a
  warning-only result

#### Scenario: Synthetic topology is covered in CI

- **WHEN** the topology laboratory passes its deterministic matrix
- **THEN** CI records the result as synthetic coverage and does not mark
  physical mixed-DPI qualification as complete
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

When visual evidence is enabled, the child SHALL bind its visual manifest,
artifacts, derived-failure records, packet/result hashes, mode, and measured
counters to the same candidate/run/scenario/attempt. Required visual evidence
or review SHALL be non-pass when missing, invalid, tampered, stale,
unavailable, suspect/defective, or unacknowledged derived failure. Optional
visual evidence SHALL remain a separate diagnostic dimension and SHALL NOT
change the native result by itself.

The first valid native or visual defect remains authoritative across
investigation reruns. A later healthy rerun SHALL be recorded as unresolved or
flake evidence, not best-of-N PASS. Synthetic/headless evidence SHALL NOT
satisfy a physical field cell requiring a supervised desktop, real input,
lease, topology, or multimodal review.

#### Scenario: A visual-enabled attempt retains bounded evidence

- **WHEN** a catalog scenario requests checkpoints or flight evidence
- **THEN** its child evidence links hash-bound visual artifacts and measured
  counters within policy limits without changing native outcome semantics

#### Scenario: A required visual artifact or collection is missing

- **WHEN** a required PNG, collection, manifest, packet, review result,
  derived-failure acknowledgement, candidate binding, or relative path fails
  verification
- **THEN** the attempt is `FAIL_HARNESS` or the declared visual gate is
  otherwise non-pass, and parent aggregation cannot infer PASS

#### Scenario: A required visual review is unavailable

- **WHEN** native checks pass but a gate requires capable review and the result
  is `REVIEW_UNAVAILABLE`
- **THEN** the visual gate remains non-pass with the missing capability named;
  native checks remain independently recorded

#### Scenario: Visual review and native qualification disagree

- **WHEN** a hash-bound review says `VISUAL_OK` while lease, identity,
  foreground, ownership, cleanup, or native assertions failed
- **THEN** the native failure/block remains authoritative and visual review
  cannot promote the attempt

#### Scenario: A visual defect is found while simple metrics pass

- **WHEN** valid reviewed screen-composited imagery is
  `VISUAL_DEFECT`/`VISUAL_SUSPECT` although geometry/brightness metrics pass
- **THEN** the finding remains in evidence and a required visual gate cannot
  pass until normal investigation/disposition

#### Scenario: Historical run has no visual section

- **WHEN** a supported historical direct/shard/parent manifest predates visual
  evidence
- **THEN** it remains valid under its original schema and no visual PASS,
  collection, artifact, or performance result is synthesized
 
#### Scenario: Release artifacts run without source edits

- **WHEN** the driver is invoked with `--configuration Release` against Release
  artifacts
- **THEN** it locates both executables, validates their candidate identities,
  resolves declared capabilities before destructive setup, and runs the
  selected catalog scenario or shard

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

- **WHEN** a physical presentation-integrity scenario has investigation reruns
- **THEN** the first valid attempt remains authoritative and a later pass after
  a valid failure is recorded as unresolved/flake evidence rather than PASS

#### Scenario: Synthetic pass does not satisfy a physical field cell

- **WHEN** a headless or synthetic presentation-integrity scenario passes but
  the matching requirement calls for real guarded input/topology
- **THEN** deterministic coverage is recorded separately and the physical cell
  remains pending or blocked until physically qualified
