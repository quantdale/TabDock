# qualification-control-plane delta — visual evidence and AI-assisted presentation review

## MODIFIED Requirements

### Requirement: Qualification manifests SHALL form a verified hierarchy

A direct scenario or shard run SHALL emit a versioned child manifest in an
isolated artifact directory. An `all` invocation SHALL create a distinct
parent run identity and manifest that imports every attempted child manifest,
verifies schema, run kind, candidate commit and executable hashes, driver
identity, shard identity, timestamps, outcomes, artifact existence, catalog
generation, and exact scenario membership. The parent SHALL record aggregate
outcome counts, capability observations, first-attempt-authoritative rerun
lineage, child-manifest hashes, and relative links to result, JUnit, timeline
artifacts, and any declared visual-evidence manifests.

When an attempt emits retained visual evidence, the child manifest SHALL index
the visual manifest by normalized relative path and SHA-256. When an AI/human
visual-review packet/result is produced, the hierarchy SHALL retain the packet
hash, result hash, visual-review verdict, and the exact candidate/run/scenario/
attempt binding without merging that verdict into the native scenario outcome.

Historical child/parent manifests that predate visual evidence SHALL remain
valid under their original supported schema. New manifests SHALL never infer
visual evidence or review from absence.

#### Scenario: A child exit code disagrees with its manifest

- **WHEN** a child process exits with a result that disagrees with its verified
  child manifest
- **THEN** the parent records `FAIL_HARNESS` for that shard and the parent run
  cannot be release PASS

#### Scenario: A partial or tampered `all` run is imported

- **WHEN** a child manifest is missing, malformed, stale, duplicated, tampered,
  assigned to the wrong shard, bound to another candidate, or absent after
  timeout/cancellation
- **THEN** the parent records `FAIL_HARNESS` and never infers success from
  process completion alone

#### Scenario: A valid full hierarchy is aggregated

- **WHEN** every declared child manifest and linked artifact passes verification
- **THEN** the parent contains every shard and scenario/attempt exactly once,
  preserves blocked/skipped/flaky outcomes, and derives its aggregate counts
  from imported evidence

#### Scenario: A child declares visual evidence

- **WHEN** a child manifest declares a visual-manifest artifact
- **THEN** parent import verifies the visual-manifest path/hash and its declared
  candidate/run/scenario/attempt identity before accepting the child evidence

#### Scenario: A visual review is present

- **WHEN** a child contains a visual-review packet/result
- **THEN** the hierarchy retains the packet/result hashes and
  `VISUAL_OK`/`VISUAL_SUSPECT`/`VISUAL_DEFECT`/
  `REVIEW_UNAVAILABLE` dimension separately from the canonical scenario
  outcome

### Requirement: Qualification bundles SHALL be portable and offline-verifiable

A qualification bundle SHALL bind a source commit, semantic version, exact
candidate executable hash, driver hash, catalog/schema generations,
run-manifest hashes, timestamps, privacy-safe OS/environment classification,
capability observations, outcome counts, and an artifact index of normalized
relative paths. An offline verifier SHALL validate all hashes, paths, required
artifacts, schema versions, candidate/source consistency, outcome summaries, and
bundle invariants without launching TabDock or trusting console output.

When visual evidence is declared, the bundle artifact index SHALL include each
visual manifest, retained PNG/contact-sheet artifact that the run requires to be
portable, and any visual-review packet/result. The verifier SHALL validate the
visual artifact bytes/hash/path/schema, packet-to-image bindings,
review-result-to-packet/image bindings, candidate/run/attempt continuity, and
visual privacy classification metadata without invoking a model or executing
returned content.

A bundle SHALL NOT silently include unrestricted desktop imagery merely because
visual evidence exists. Portable inclusion follows the run's explicit visual
privacy/retention policy.

#### Scenario: Evidence bytes are modified after capture

- **WHEN** a referenced manifest, result, JUnit file, timeline, visual PNG,
  visual-review packet/result, or driver artifact is changed or removed
- **THEN** offline verification fails and identifies the relative artifact and
  violated hash/existence contract

#### Scenario: An unsafe bundle path is supplied

- **WHEN** an artifact index contains an absolute path, traversal segment,
  duplicate path, or path that resolves outside the bundle root
- **THEN** offline verification fails before reading or accepting the artifact
  as qualification evidence

#### Scenario: An unsupported bundle schema is supplied

- **WHEN** the bundle uses a future schema or a schema outside the explicitly
  accepted migration window
- **THEN** verification returns a deterministic unsupported-schema failure
  rather than partially accepting the bundle

#### Scenario: A reviewed screenshot no longer matches the result

- **WHEN** a visual-review result references a packet/image hash whose bytes in
  the bundle differ from the reviewed evidence
- **THEN** offline verification rejects the review and the bundle cannot claim
  the associated visual-review gate

#### Scenario: Historical bundle contains no visual evidence

- **WHEN** a supported historical qualification bundle predates the visual
  schemas
- **THEN** it remains verifiable under its declared historical schema and no
  visual PASS/review is synthesized

### Requirement: Qualification planning and remote evidence import SHALL be defensive

The driver SHALL provide a no-input planning mode that explains catalog
scenarios required for a requested gate and classifies each as runnable,
blocked, skipped, or optional from a privacy-safe capability snapshot.
Independent-machine reports SHALL be structured, candidate-bound,
hash-verifiable, and importable without executing returned scripts or binaries;
imported evidence SHALL retain its machine identity and synthetic/physical
classification.

Planning SHALL also report a scenario/gate's declared visual-evidence level,
whether multimodal visual review is required, and whether the current execution
environment can collect the required approved capture scopes. Planning SHALL
not invoke an AI reviewer, launch applications, or capture the desktop.

Independent-machine reports MAY return visual artifacts and visual-review
records only when the package/run policy explicitly permits them. Import SHALL
verify their paths, hashes, schemas, privacy classifications, and candidate/run
bindings as untrusted data and SHALL never execute an included reviewer,
script, model adapter, or binary.

#### Scenario: A second machine returns an untrusted report

- **WHEN** an imported report contains malformed fields, a candidate/source/hash
  mismatch, an unsupported OS classification, a future timestamp, executable/
  script content, or invalid visual-evidence/review bindings
- **THEN** import fails closed and no external release gate is changed

#### Scenario: Planning runs on an unsafe desktop

- **WHEN** planning is requested without sending input
- **THEN** it emits a machine-readable plan with capability and visual-policy
  blocks and never starts TabDock, a guest, a physical scenario, screen
  recording, or a multimodal reviewer

#### Scenario: Remote report includes restricted visual imagery without policy

- **WHEN** a returned package contains real-app/desktop-restricted screenshots
  that the originating package/run policy did not authorize
- **THEN** import rejects those artifacts and the related visual gate remains
  non-pass
