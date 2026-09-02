# validation-qualification Specification

## Purpose

Defines the hermetic and supervised qualification boundary: deterministic
catalog, manifest, topology, and bundle checks run in CI, while real-input and
hardware behavior remains explicitly capability- and lease-gated.
## Requirements
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

### Requirement: Physical topology qualification SHALL be exact-candidate, supervised, and reversible

A physical monitor/DPI cell SHALL run only after capability planning proves the
exact candidate, supervision, input lease, topology, effective DPI, identity,
foreground, and point ownership. Temporary layout/scale state SHALL be
snapshotted, verified, restored, and re-verified.

The harness SHALL NOT use registry hacks, blind Display Settings input,
unrelated-window manipulation, unsupported display mutation, or destructive
automated monitor hot-unplug.

#### Scenario: Temporary left-negative layout is qualified
- **WHEN** the operator establishes and verifies a left-negative secondary and the exact-candidate cell passes
- **THEN** before/after topology snapshots and restoration evidence are required for physical acceptance

#### Scenario: Restore cannot be proven
- **WHEN** temporary display state cannot be verified as restored
- **THEN** further physical input stops and the campaign records an environment/harness block

#### Scenario: Requested physical scale is unavailable
- **WHEN** planning cannot prove 150%, 175%, or 200% physical DPI
- **THEN** the cell blocks before input and cannot inherit synthetic PASS

### Requirement: Real-app qualification SHALL be exact-candidate, supervised, and first-attempt authoritative

Every real-app scenario (Chromium/Notepad/Terminal) SHALL declare capability needs (guest family `browser` or `real-app`, `--guest` value, monitor/DPI, supervision, lease, destructive-state `ExternalBrowser`/`UserOwnedExternal`), SHALL run only after `DesktopQualificationLease` proves candidate/executable/driver/run/scenario/attempt, topology, effective DPI, identity, foreground, and point ownership, and SHALL preserve the first valid attempt across `--reruns` (a valid `FAIL_PRODUCT` followed by `PASS` is `FLAKE_UNCLASSIFIED`, never best-of-N PASS).

`FLAKE_UNCLASSIFIED` SHALL be a provisional investigation disposition, not a final closure disposition. A final closure SHALL record a defensible classification for every preserved first valid `FAIL_PRODUCT` (`PROVEN_PRODUCT_DEFECT`, `PROVEN_HARNESS_DEFECT`, `PROVEN_ENVIRONMENT_FAILURE`, `CHARACTERIZED_PRODUCT_FLAKE`, or `NOT_REPRODUCED_BUT_UNEXPLAINED`); `CHARACTERIZED_PRODUCT_FLAKE` and `NOT_REPRODUCED_BUT_UNEXPLAINED` SHALL leave the closure open. Fifteen later PASS cycles SHALL NOT erase a valid historical failure.

Synthetic fixtures SHALL NOT satisfy a real-app gate. The canonical final gates (`scripts/validate.ps1 -Configuration Release -Ci -Publish`, the explicit `--selftest-native-abi` ABI probe, and the deterministic resource-headless gate with the canonical seed/cycle count) SHALL be actually executed against the exact final candidate and their results recorded; an inferred or substituted subset SHALL NOT be recorded as completion.

#### Scenario: First valid real-app failure is retained
- **WHEN** attempt 1 of `browser-fullscreen-contained` is `FAIL_PRODUCT` and attempt 2 is `PASS`
- **THEN** the recorded disposition is `FLAKE_UNCLASSIFIED` with both run/packet hashes retained, not ordinary `PASS`

#### Scenario: Unexplained first failure blocks final closure
- **WHEN** an archive-bound campaign holds a preserved first valid `FAIL_PRODUCT` whose cause is not proven to be product, harness, or environment
- **THEN** the final closure remains open and the investigation names the classification gap rather than archiving a "final closure"

#### Scenario: Real-app cell blocks before input when harness proof is absent
- **WHEN** foreground/point ownership or candidate/lease cannot be proven for a real-app HWND
- **THEN** the scenario records `BLOCKED_ENVIRONMENT`/`BLOCKED_CAPABILITY` without sending input

#### Scenario: Canonical final gates are executed, not inferred
- **WHEN** a campaign claims its final validation task complete
- **THEN** `validate.ps1 -Configuration Release -Ci -Publish`, the native ABI probe, and the resource-headless gate have actually run against the exact final candidate with recorded exit codes and evidence

### Requirement: Real-app qualification SHALL produce a physical acceptance matrix

The campaign SHALL emit a durable matrix at `.agent/investigations/real-app-hardening-acceptance-matrix-2026-09-02.md` (or JSON per convention) with at least: app, executable, process-start identity, HWND/root, run-owned/adopted, scenario, attempt, source/destination monitor/DPI, lease, foreground, point ownership, native outcome, visual outcome, packet hash, cleanup result, final disposition, blocker/reason. Unavailable apps/cells remain visible as capability blocks.

#### Scenario: Matrix contains unavailable browser family
- **WHEN** Brave is not installed
- **THEN** Brave rows remain `SKIP_CAPABILITY`/`BLOCKED_CAPABILITY` with reason `executable not found`, not omitted or fabricated `PASS`

### Requirement: Product repair SHALL be gated by valid real-app failure or deterministic policy defect

Production edits SHALL be permitted only when (a) a valid real-app `FAIL_PRODUCT` is established with frozen first evidence, or (b) deterministic coverage proves a real invariant defect. Forbidden repairs include `SetParent`/reparenting, style stripping, permanent topmost, global z-order polling, blind repeated `SetWindowPos`, killing adopted apps, process-name-only ownership, title-based identity, and relaxed foreground/point checks.
#### Scenario: Harness failure does not authorize a production change
- **WHEN** a real-app run fails as `FAIL_HARNESS` (e.g., picker cannot prove Notepad broker generation)
- **THEN** no production TabDock behavior is edited; the harness is fixed and the cell is requalified

