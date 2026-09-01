# visual-qualification-evidence delta — closure and strict evidence contracts

## MODIFIED Requirements

### Requirement: AI visual review SHALL be hash-bound and verifiable

A structured visual review result SHALL bind the exact review-packet hash,
candidate, run, scenario, attempt, reviewed artifact IDs, reviewed image hashes,
and any acknowledged derived-artifact failures. Offline verification SHALL
compute the packet SHA-256 from the exact packet bytes on disk and compare that
value with the result, the enclosing visual manifest, and the qualification
hierarchy. It SHALL reject an empty placeholder, malformed hash, missing packet
link where a review is claimed, changed or missing evidence, another
candidate/run, unknown checkpoint or artifact IDs, malformed regions,
unsupported schemas, unreviewed required images, or a packet/result/manifest
identity disagreement.

Before implementation changes the existing `packetSha256: string.Empty`
literal, the campaign SHALL trace every writer, test fixture, verifier branch,
and bundle index using the field and classify whether the literal is a
verifier-only/test-only defect or can weaken an actual packet-binding gate.
The final implementation SHALL use the computed packet hash, never the empty
literal or a caller-supplied substitute.

#### Scenario: A correctly bound result verifies

- **WHEN** a result names the exact packet bytes and every reviewed image hash
  matches the current run identity
- **THEN** offline verification accepts the review and records the binding
  without inferring any native qualification fact

#### Scenario: An empty packet hash is supplied

- **WHEN** a result or verifier path contains an empty packet hash, including
  the reported literal, instead of the computed packet SHA-256
- **THEN** verification rejects the result and identifies the packet-binding
  invariant that failed

#### Scenario: A packet byte changes after review

- **WHEN** one packet byte changes while the result retains its old hash
- **THEN** offline verification rejects the result and the visual gate cannot
  claim review completion

#### Scenario: Screenshot changes after AI review

- **WHEN** any reviewed image byte changes after the result was produced
- **THEN** the result fails verification and cannot contribute visual evidence

#### Scenario: Review result omits a required checkpoint

- **WHEN** a gate requires visual review of declared checkpoints but the result
  omits one or substitutes another artifact
- **THEN** the review is incomplete and the required visual gate remains
  non-pass

### Requirement: Visual-review acceptance SHALL be demonstrated with non-vacuous controlled fixtures

Visual-review acceptance SHALL include an exact-candidate supervised desktop
record and at least one healthy controlled packet, one test-owned seeded visual
defect packet, and one transient/flight-recorder failure packet. A capable
multimodal agent SHALL actually open the retained contact/raw images and emit
valid hash-bound results. The healthy packet SHALL not be falsely flagged, the
seeded defect SHALL be detected without its identity being disclosed by file
name or review prompt, and a non-vision path SHALL emit `REVIEW_UNAVAILABLE`
without inferring from names, metrics, logs, or assumptions. First valid visual
defects SHALL remain authoritative across investigation reruns; a later pass
is not a best-of-N visual PASS.

#### Scenario: A healthy packet is reviewed

- **WHEN** the capable reviewer inspects the healthy packet and correlated
  evidence
- **THEN** it produces a valid hash-bound `VISUAL_OK` result unless the image
  is genuinely ambiguous, and does not invent a defect

#### Scenario: A seeded defect is reviewed

- **WHEN** the capable reviewer inspects a test-owned packet containing a
  seeded occlusion, wrong-guest, clipping, or misalignment defect without a
  disclosure in the prompt
- **THEN** it produces a valid hash-bound `VISUAL_DEFECT` result identifying
  the observable checkpoint/image and finding

#### Scenario: Seeded occlusion packet is reviewed

- **WHEN** a capable agent reviews a controlled packet containing a deliberately
  occluded guest presentation without being told which image is defective
- **THEN** it identifies the visual problem and emits a valid hash-bound
  `VISUAL_DEFECT` result

#### Scenario: Seeded healthy packet is reviewed

- **WHEN** the same workflow reviews a controlled healthy presentation packet
- **THEN** it does not fabricate a defect and can emit a valid
  `VISUAL_OK` result subject to the normal native-evidence boundary

#### Scenario: The reviewer cannot inspect images

- **WHEN** the selected harness has no usable vision capability
- **THEN** it emits `REVIEW_UNAVAILABLE` with bounded provenance and the gate
  remains non-pass when review is required

### Requirement: Visual-review-required gates SHALL fail honestly when review is unavailable

A catalog scenario, shard, or release gate MAY declare visual review and
required derived artifacts. For such a gate, missing, invalid, tampered,
stale, unreviewed, `REVIEW_UNAVAILABLE`, suspect/defective, or unacknowledged
derived-artifact failure evidence SHALL remain non-pass. An optional
contact-sheet failure MAY be recovered by reviewing intact raw images only
when the structured result acknowledges the stable derived-failure ID; it
cannot silently become `VISUAL_OK`. Scenarios that do not require review MAY
retain diagnostic images and explicit unavailable/derived-failure metadata
without changing their ordinary native outcome.

#### Scenario: Required review is unavailable

- **WHEN** a gate requires multimodal review but the reviewer reports
  `REVIEW_UNAVAILABLE`
- **THEN** the gate names the unavailable capability and remains non-pass while
  native checks remain independently recorded

#### Scenario: Contact-sheet generation fails

- **WHEN** raw PNGs are valid but the requested derived contact sheet fails
- **THEN** raw artifact hashes remain verifiable, a dedicated derived-failure
  record is emitted, and `VISUAL_OK` is forbidden unless policy permits raw-only
  review and the result explicitly acknowledges that record

#### Scenario: Optional review is unavailable

- **WHEN** a scenario collects visual evidence for diagnosis but its release
  contract does not require a visual review
- **THEN** `REVIEW_UNAVAILABLE` is recorded informationally and ordinary
  qualification semantics remain intact

## ADDED Requirements

### Requirement: Required visual collections SHALL be strict and non-null

Current visual packet, review-result, and manifest schemas SHALL distinguish
required collections from optional collections. Required collections SHALL be
present, non-null, and element-valid after JSON deserialization; omitted or
JSON-`null` values SHALL fail deterministic validation. Empty is valid only for
fields whose schema explicitly permits empty (including the documented
`REVIEW_UNAVAILABLE` result fields). The implementation SHALL use a strict
constructor/converter or concrete non-null collection contract and SHALL not
hide malformed evidence behind nullable `IReadOnlyList<T>` defaults or compiler
warning suppression. Historical non-visual schemas SHALL remain valid without
fabricating visual collections or verdicts.

#### Scenario: A required collection is omitted

- **WHEN** a current packet, manifest, or result omits a required collection
- **THEN** deserialization/validation rejects it as malformed evidence

#### Scenario: A current collection is JSON null

- **WHEN** a required collection is explicitly `null`
- **THEN** validation rejects it and no visual PASS is produced

#### Scenario: A historical non-visual manifest has no visual fields

- **WHEN** a supported historical bundle predates visual evidence
- **THEN** it remains valid under its declared schema and no visual evidence is
  synthesized

### Requirement: Derived visual artifact failures SHALL be explicit and preserve raw evidence

When a derived contact sheet or other explicitly requested derived visual
artifact cannot be built, the enclosing visual manifest SHALL emit a stable
`derivedArtifactFailures` record containing artifact kind/ID, checkpoint,
scenario/attempt binding, bounded failure reason, requiredness, timestamp, and
whether valid raw source artifacts were preserved. The scenario result and
review packet SHALL expose the same failure ID/count or an explicit successful
reference. Offline verification SHALL validate the record and re-hash the raw
sources. A derived failure SHALL never overwrite, delete, or mutate an
authoritative raw PNG, and SHALL never disappear into a generic capture
unavailable count.

#### Scenario: Derived output fails after raw capture

- **WHEN** raw visual artifacts are valid but contact-sheet encode, decode,
  budget, or filesystem work fails
- **THEN** raw artifacts remain immutable and verifiable, the derived failure
  is indexed explicitly, and visual acceptance follows its requiredness/
  acknowledgement policy rather than silently passing

#### Scenario: A derived failure record is tampered

- **WHEN** its ID, reason, requiredness, source binding, or path is changed
- **THEN** offline verification rejects the visual manifest/review
