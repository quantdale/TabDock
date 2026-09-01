# qualification-control-plane delta — visual artifacts and measured resource evidence

## MODIFIED Requirements

### Requirement: Qualification manifests SHALL form a verified hierarchy

A direct scenario or shard run SHALL emit a versioned child manifest in an
isolated artifact directory. An `all` invocation SHALL create a distinct
parent run identity and manifest that imports every attempted child manifest,
verifies schema, run kind, candidate commit and executable hashes, driver
identity, shard identity, timestamps, outcomes, artifact existence, catalog
generation, exact scenario membership, and visual/performance evidence when
declared. The parent SHALL record aggregate outcome counts, capability
observations, first-attempt-authoritative rerun lineage, child-manifest hashes,
and relative links to result, JUnit, timeline, visual manifest, review, and
resource artifacts.

When a child emits visual evidence, the hierarchy SHALL index the visual
manifest and every required retained/derived artifact by normalized relative
path and SHA-256, verify candidate/run/scenario/attempt continuity, preserve
explicit unavailable and derived-failure records, and retain packet/result
hashes and visual verdict separately from native outcome. When a child emits
performance evidence, the hierarchy SHALL retain mode, sample/distribution
summary, selected budgets, resource comparison, and synthetic/physical
classification.

Historical child/parent manifests that predate visual or performance evidence
SHALL remain valid under their original supported schema. New manifests SHALL
never infer visual/performance evidence or review from absence.

#### Scenario: A valid full hierarchy is aggregated

- **WHEN** every declared child manifest and linked artifact passes its
  schema/hash/path/identity checks
- **THEN** the parent contains each shard and scenario/attempt exactly once,
  preserves blocked/skipped/flaky outcomes, retains visual verdicts and
  measured-resource dimensions separately, and derives counts from imported
  evidence

#### Scenario: A visual artifact or performance record is stale

- **WHEN** a child visual/resource artifact is missing, malformed, duplicated,
  path-escaping, tampered, stale, or bound to another candidate/run/attempt
- **THEN** parent import records `FAIL_HARNESS` or the declared gate's non-pass
  result and never infers success from process completion

#### Scenario: Historical hierarchy contains no visual fields

- **WHEN** a supported historical child/parent manifest has no visual or
  performance section
- **THEN** it remains verifiable under its declared schema and no visual,
  packet, review, or resource PASS is synthesized

 
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
### Requirement: Qualification bundles SHALL be portable and offline-verifiable

A qualification bundle SHALL bind a source commit, semantic version, exact
candidate executable hash, driver hash, catalog/schema generations,
run-manifest hashes, timestamps, privacy-safe OS/environment classification,
capability observations, outcome counts, and an artifact index of normalized
relative paths. An offline verifier SHALL validate all hashes, paths, required
artifacts, schema versions, candidate/source consistency, outcome summaries,
and bundle invariants without launching TabDock, a guest, a model, or trusting
console output.

When visual evidence is declared, the artifact index SHALL include each visual
manifest, required retained raw PNG/contact-sheet artifact, derived-failure
record, and visual-review packet/result. Verification SHALL re-hash visual
bytes and validate strict required collections, packet-to-image bindings,
review-result-to-packet/image bindings, manifest packet links,
candidate/run/scenario/attempt continuity, privacy classes, and the rule that
unacknowledged required or derived visual failures cannot claim PASS. When
performance evidence is declared, the index SHALL include the measurement
report and selected budget/provenance record.

A bundle SHALL NOT silently include unrestricted desktop imagery merely because
visual evidence exists. Portable inclusion follows the explicit visual
privacy/retention policy. Historical bundles without visual fields remain
valid under their original schema and do not synthesize visual evidence.

#### Scenario: Evidence bytes are modified after capture

- **WHEN** a referenced manifest, result, JUnit, timeline, visual PNG,
  contact-sheet, review packet/result, measurement report, or driver artifact
  changes or is removed
- **THEN** offline verification fails and identifies the relative artifact and
  violated hash/existence contract

#### Scenario: An unsafe or duplicate path is supplied

- **WHEN** an artifact index contains an absolute path, traversal segment,
  duplicate path, or path that resolves outside the bundle root
- **THEN** verification fails before accepting the artifact as evidence

 
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
#### Scenario: A visual review packet/result is stale

- **WHEN** packet/result hashes, strict collections, image bindings, derived
  failure acknowledgements, candidate/run/attempt identity, or manifest links
  disagree
- **THEN** offline verification rejects the visual review and the related gate
  cannot claim PASS

#### Scenario: Historical bundle contains no visual evidence

- **WHEN** a supported historical qualification bundle predates visual and
  performance schemas
- **THEN** it remains verifiable under its declared historical schema and no
  visual/performance PASS is synthesized

### Requirement: Qualification planning and remote evidence import SHALL be defensive

The driver SHALL provide a no-input planning mode that explains catalog
scenarios required for a requested gate and classifies each as runnable,
blocked, skipped, or optional from a privacy-safe capability snapshot.
Independent-machine reports SHALL be structured, candidate-bound,
hash-verifiable, and importable without executing returned scripts or
binaries; imported evidence SHALL retain its machine identity and
synthetic/physical classification.

Planning SHALL report the declared visual-evidence mode, packet/review
requiredness, performance measurement mode/budget availability, and whether
the current environment can collect approved scopes and resource signals.
Planning SHALL not invoke a model, launch applications, capture the desktop, or
claim that a measured budget or supervised visual acceptance exists.

#### Scenario: A remote report contains untrusted visual/resource evidence

- **WHEN** an imported report contains malformed fields, candidate/source/hash
  mismatch, unsupported environment, future timestamp, executable/script
  content, path/privacy violation, or invalid visual/performance binding
- **THEN** import fails closed and no external release gate changes

#### Scenario: Planning runs without input

- **WHEN** a visual/performance plan is requested
- **THEN** it emits policy/capability/budget blocks without starting TabDock,
  sending input, recording the desktop, sampling a process, or invoking a
  multimodal reviewer
 
#### Scenario: A second machine returns an untrusted report

- **WHEN** an imported report contains malformed fields, a candidate/source/hash
  mismatch, an unsupported OS classification, a future timestamp, or
  executable/script content
- **THEN** import fails closed and no external release gate is changed

#### Scenario: Planning runs on an unsafe desktop

- **WHEN** planning is requested without sending input
- **THEN** it emits a machine-readable plan with capability blocks and never
  starts TabDock, a guest, or a physical scenario
