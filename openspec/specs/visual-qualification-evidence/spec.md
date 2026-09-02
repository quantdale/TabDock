# visual-qualification-evidence Specification

## Purpose
TBD - created by archiving change 2026-08-31-visual-evidence-ai-review. Update Purpose after archive.
## Requirements
### Requirement: ValidationDriver SHALL retain bounded visual checkpoints as verifiable artifacts

When a scenario requests visual evidence, ValidationDriver SHALL capture only
the declared approved region(s), encode retained frames as immutable lossless
artifacts, and bind every image to the exact candidate, run, scenario, attempt,
checkpoint, capture scope, timestamp, screen rectangle, monitor/DPI context,
capture method, and SHA-256. Required visual artifacts SHALL be indexed by the
scenario/run evidence hierarchy and SHALL be verifiable offline.

Visual capture SHALL be bounded by explicit count/byte/policy limits and SHALL
not silently expand a target/window capture into unrestricted desktop capture.

#### Scenario: A presentation checkpoint is retained

- **WHEN** a visual-enabled scenario reaches a declared checkpoint after its
  bounded settle condition
- **THEN** the requested approved region is retained as a PNG with a unique
  artifact/checkpoint identity and hash-bound metadata linked from that attempt

#### Scenario: A required screenshot cannot be produced

- **WHEN** a checkpoint declares visual capture required but acquisition,
  encoding, artifact finalization, or registration fails
- **THEN** the scenario records `FAIL_HARNESS` and SHALL NOT claim visual
  qualification from the remaining native/log evidence alone

#### Scenario: An optional screenshot cannot be produced

- **WHEN** a checkpoint declares visual capture best-effort and the image cannot
  be retained
- **THEN** explicit visual-unavailable metadata is recorded while the ordinary
  native scenario result remains independently evaluated

#### Scenario: A visual artifact is tampered after capture

- **WHEN** a retained PNG or its candidate/run/checkpoint binding is modified,
  removed, duplicated, path-escaped, or hash-mismatched
- **THEN** offline verification rejects the visual evidence and any review that
  references it

### Requirement: Visual capture SHALL distinguish screen composition from direct-window rendering

Visual evidence SHALL record whether pixels came from the DWM-composited screen
region or a direct-window rendering path such as
`PrintWindow(PW_RENDERFULLCONTENT)`. Presentation/occlusion claims SHALL use
screen-composited evidence where the question is what the user actually saw.
Direct-window rendering MAY be retained as secondary evidence to distinguish a
healthy window back-buffer from an unhealthy screen composition.

#### Scenario: Direct render is healthy but screen composition is covered

- **WHEN** the direct-window capture contains live expected guest content while
  the DWM-composited capture over the same presentation region shows an opaque
  covering surface
- **THEN** the evidence records a composition/presentation discrepancy and
  SHALL NOT call the guest visually healthy merely because direct rendering
  succeeded

#### Scenario: Capture methods disagree because one path is unsupported

- **WHEN** one capture method is unavailable or known-unreliable for a guest
- **THEN** the unavailable method is identified explicitly and the remaining
  method is not mislabeled as equivalent evidence

### Requirement: Visual checkpoints SHALL be semantic and event-driven

Scenarios SHALL request visual checkpoints by stable checkpoint/action/assertion
identity and declared visual expectation. Ordinary visual qualification SHALL
capture around meaningful state transitions rather than continuously recording
the desktop. Supported phases SHALL include baseline, pre-action, immediate
post-action, settled post-action, suspicious/failure, final healthy state, and
pre-cleanup where relevant.

#### Scenario: Rename presentation is visually checkpointed

- **WHEN** a rename scenario enables checkpoint evidence
- **THEN** it can retain a baseline, rename-open settled state, post-commit or
  post-cancel settled state, and failure image without scenario code managing
  PNG paths or hashes directly

#### Scenario: A scenario is visually healthy

- **WHEN** all declared presentation-sensitive transitions settle without
  suspicion or failure
- **THEN** the run retains only its configured semantic checkpoints and does
  not create an unbounded frame sequence

### Requirement: High-risk transitions MAY use a bounded visual flight recorder

A scenario MAY explicitly enable a low-rate in-memory visual ring buffer around
a high-risk transition. The recorder SHALL enforce hard frame-count,
frame-rate, duration, and memory ceilings; SHALL discard rolling history by
default on healthy completion; SHALL flush only the bounded relevant history
when a suspicious/failure event requests it; and SHALL stop in all cleanup,
exception, timeout, cancellation, and abort paths.

The flight recorder SHALL NOT be an always-on desktop recorder.

#### Scenario: A transient defect occurs before the settled checkpoint

- **WHEN** a high-risk transition briefly presents the wrong/blank/covered
  visual state and then reaches a later assertion failure or suspicious trigger
- **THEN** the recorder retains the bounded pre-trigger sequence plus the
  trigger frame with relative timing so the transient state can be inspected

#### Scenario: A high-risk transition stays healthy

- **WHEN** a flight-recorder-enabled transition completes without a flush
  trigger
- **THEN** rolling history is discarded according to policy and no unbounded
  video-like artifact remains

### Requirement: Visual evidence SHALL have a restrictive privacy boundary

Routine visual capture SHALL prefer run-owned/test-owned TabDock, GuineaPig,
guest, popup, and tightly bounded target/context regions. Whole-virtual-desktop
capture SHALL be disabled by default. Real-application and desktop-restricted
imagery SHALL require explicit policy classification and SHALL NOT be added to
support bundles, ordinary CI artifacts, source control, or remote model uploads
implicitly.

Every capture SHALL record a privacy classification and whether the actual
rectangle may include pixels outside test/product-owned surfaces.

#### Scenario: Routine controlled-fixture capture is enabled

- **WHEN** a GuineaPig/TabDock scenario requests visual checkpoints
- **THEN** only the declared test/product-owned regions are captured and the
  artifact records that privacy scope

#### Scenario: An unrestricted desktop capture is not explicitly authorized

- **WHEN** a scenario requests or would require full-desktop imagery under
  normal visual-evidence policy
- **THEN** capture is refused or constrained to the approved target scope rather
  than silently recording unrelated user activity

#### Scenario: A real-app visual packet is requested

- **WHEN** browser/Notepad/other adopted-app imagery is required for a physical
  diagnostic
- **THEN** the capture is marked restricted, minimized/cropped to the necessary
  region, retained only under explicit policy, and remains separate from
  default CI/support-bundle behavior

### Requirement: ValidationDriver SHALL produce a vendor-neutral AI visual-review packet

For an attempt selected for multimodal review, ValidationDriver SHALL generate a
bounded versioned packet containing exact candidate/run/scenario/attempt
identity, ordered checkpoint records, raw image relative paths and hashes,
optional derived contact-sheet reference, declared visual expectations,
relevant native/UIA/pixel/timeline correlations, environment-variation notes,
and the required review-result schema/output binding.

The packet SHALL NOT require or embed a specific AI provider SDK, API key, or
network service.

#### Scenario: A multimodal coding agent reviews a run

- **WHEN** a capable development agent receives a valid review packet
- **THEN** it can open the retained contact sheet/raw PNGs using its own vision
  facility, evaluate expected-versus-observed presentation, and emit a
  structured review tied to the exact packet/image hashes

#### Scenario: The active agent cannot inspect images

- **WHEN** the development harness/model has no usable image-input capability
- **THEN** review records `REVIEW_UNAVAILABLE` rather than inferring a visual
  verdict from file names, logs, pixel metrics, or assumptions

#### Scenario: A packet is copied to another run

- **WHEN** a review packet's candidate/run/scenario/attempt or referenced image
  hashes do not match the current evidence
- **THEN** the packet/review is rejected as stale or invalid

### Requirement: AI visual review SHALL use a separate structured verdict vocabulary

AI or human visual review SHALL record one of
`VISUAL_OK`, `VISUAL_SUSPECT`, `VISUAL_DEFECT`, or
`REVIEW_UNAVAILABLE`. Concrete findings SHALL reference exact checkpoint and
artifact IDs/hashes, describe observable expected-versus-actual presentation,
and MAY include category, severity, confidence, uncertainty, and normalized
image region. The review SHALL NOT require hidden chain-of-thought.

Review verdicts SHALL remain separate from the canonical physical scenario
outcome vocabulary.

#### Scenario: Reviewed images show a concrete occlusion defect

- **WHEN** a valid review finds that an opaque surface visibly covers a guest
  region that the checkpoint expectation requires to remain presented
- **THEN** the review records `VISUAL_DEFECT` with the exact checkpoint/image
  references and the scenario cannot be accepted as visually qualified until
  the finding is dispositioned

#### Scenario: Reviewed imagery is ambiguous

- **WHEN** the image may reflect either a capture artifact or a real visual
  defect and available evidence does not resolve the distinction
- **THEN** the review records `VISUAL_SUSPECT` with uncertainty rather than
  forcing OK/DEFECT

#### Scenario: A review says the frame looks healthy

- **WHEN** the reviewer records `VISUAL_OK`
- **THEN** that verdict contributes only visual-review evidence and does not
  prove native process identity, desktop lease, foreground ownership, geometry,
  cleanup, or candidate provenance

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

### Requirement: Visual review SHALL augment but never override native safety and qualification evidence

Visual review SHALL NOT override a failed/blocked desktop lease, process/HWND
identity mismatch, point-ownership failure, invalid foreground, cleanup failure,
native assertion failure, or other canonical qualification prerequisite.
Likewise, a visible defect with otherwise valid prerequisites SHALL remain
actionable even if simple numeric pixel/geometry metrics pass.

A visual defect SHALL trigger normal evidence correlation before any production
root-cause or source-change claim is authorized.

#### Scenario: AI says OK but native safety failed

- **WHEN** a reviewer records `VISUAL_OK` but the underlying physical attempt
  lost its lease or identity continuity
- **THEN** the scenario remains failed/blocked according to the native outcome
  and visual review cannot promote it

#### Scenario: AI shows a defect while geometry metrics pass

- **WHEN** retained screen-composited imagery visibly violates the declared
  presentation expectation while guest rectangles/brightness metrics look
  nominal
- **THEN** the run retains the visual finding and begins a normal defect/harness
  investigation rather than accepting a metrics-only visual PASS

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

### Requirement: Physical topology visual evidence SHALL be topology-bound and scope-restricted

Physical topology checkpoints SHALL bind retained images to exact candidate,
run/scenario/attempt, topology snapshot, target monitor, effective DPI, and
approved capture scope. Before/after evidence SHOULD cover title centering, one
mixed-DPI transfer, one controlled topmost interaction, and one
maximize/restore transition where available.

Whole-desktop capture SHALL NOT be implicitly enabled to make a topology test
pass.

#### Scenario: Mixed-DPI before/after images are reviewed
- **WHEN** a supervised transfer retains approved before/after checkpoints
- **THEN** packet/result verification proves both images match the same attempt and expected source/destination topology/DPI

#### Scenario: Synthetic topology image is submitted to physical gate
- **WHEN** a fixture/laboratory image has synthetic topology provenance
- **THEN** the physical mixed-DPI visual gate remains non-pass even if review is VISUAL_OK

### Requirement: Adopted real-app visuals SHALL use REAL_APP_RESTRICTED capture

Routine real-app capture SHALL prefer the minimal approved region needed to prove presentation: host client plus bounded context around the guest, never whole virtual desktop. `REAL_APP_RESTRICTED` SHALL be the privacy class for adopted-browser/Notepad/Terminal imagery; `TEST_OWNED`/`PRODUCT_OWNED` remain for GuineaPigs and chrome.

Every frame SHALL record requested/actual rectangle, monitor/DPI, method (`BitBlt` screen composition vs `PrintWindow`), target identity, privacy, and duration. Whole-desktop capture SHALL be disabled by default and SHALL NOT be added to support bundles, ordinary CI artifacts, source control, or model uploads implicitly.

#### Scenario: Real-app default capture is minimized
- **WHEN** `browser-fullscreen-contained` requests visual evidence for an adopted Chromium window
- **THEN** only the host client and bounded guest/context regions are retained with `privacyClass=REAL_APP_RESTRICTED`; no unrelated desktop or URL content beyond the controlled test page is recorded

#### Scenario: Adopted app visual privacy is enforced
- **WHEN** a scenario would require capturing personal documents, terminal history, or unrelated windows to prove a real-app cell
- **THEN** capture is refused or constrained to the controlled blank/test content; whole-desktop capture remains disabled and the result is `BLOCKED_CAPABILITY` or `REVIEW_UNAVAILABLE` as appropriate

### Requirement: Real-app visual packets SHALL be hash-bound and separately reviewed

A real-app visual packet SHALL bind exact candidate/run/scenario/attempt, HWND/process-start identity, topology/DPI, packet hash, artifact hashes, checkpoint expectations, and privacy scope. A capable multimodal agent SHALL inspect retained `REAL_APP_RESTRICTED` images via the canonical workflow `.agent/workflows/visual-evidence-review.md`; verdicts `VISUAL_OK`/`VISUAL_SUSPECT`/`VISUAL_DEFECT`/`REVIEW_UNAVAILABLE` remain separate from native outcomes and SHALL NOT promote a failed lease/identity/presentation.

For every installed Chromium browser (Chrome, Edge, Brave), at least one accepted exact-candidate physical F11 attempt SHALL actually produce the required raw PNG checkpoints (baseline captured state, immediately before F11, fullscreen state, post-containment/restored state, and a settled fifth where helpful), a visual manifest, a review packet, a packet SHA-256 computed from the exact packet bytes, verified selected image hashes, matching candidate/run/scenario/attempt/topology/monitor/privacy-class bindings, and a verifier result of `Valid:true`. Capability planning, future-packet intent, or "visual implementation exists" wording SHALL NOT substitute for the packet itself. Browser profiles SHALL be test-owned with controlled isolated content only; whole-desktop capture remains disabled and unrelated windows/personal content are never captured.

#### Scenario: Restricted browser packet is reviewed
- **WHEN** a valid `browser-fullscreen-contained` packet retains before/fullscreen/after images with `Valid:true` native and `REAL_APP_RESTRICTED` scope
- **THEN** a reviewer can hash-verify the packet/result, inspect raw PNGs, and record `VISUAL_OK` only when the visual contract is met; `REVIEW_UNAVAILABLE` remains `BLOCKED_CAPABILITY` for a required visual gate

#### Scenario: Installed Chromium browsers produce real packets
- **WHEN** Chrome, Edge, and Brave are installed and a final real-app closure claims visual acceptance
- **THEN** each browser has an existing hash-verified packet with real raw PNGs and verifier `Valid:true`; no browser closes on intent alone

### Requirement: Real-app visual tamper SHALL be rejected

Offline verification SHALL reject any packet/result whose PNG bytes, packet hash, candidate/run binding, checkpoint artifact IDs, or privacy scope was modified, removed, duplicated, path-escaped, or re-bound to another run. At least one accepted real Chromium packet SHALL be tamper-exercised: a copy in a temporary validation root with one altered PNG byte SHALL be deterministically rejected by the offline verifier, and the exact rejection reason SHALL be recorded. The authoritative packet SHALL NOT be modified.

#### Scenario: Real-app packet is tampered
- **WHEN** a retained real-app PNG is modified after capture
- **THEN** verification returns `FAIL_HARNESS`-equivalent and the associated visual gate cannot claim `PASS`

#### Scenario: Tamper rejection is exercised on a real packet
- **WHEN** a final visual closure is claimed
- **THEN** at least one accepted real Chromium packet copy with a one-byte PNG alteration is deterministically rejected and the rejection reason is recorded, while the authoritative packet remains unmodified

