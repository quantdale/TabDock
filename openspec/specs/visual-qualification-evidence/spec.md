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
candidate, run, scenario, attempt, reviewed artifact IDs, and reviewed image
hashes. Offline verification SHALL reject a result that references missing or
changed evidence, another candidate/run, unknown checkpoint IDs, malformed
regions, unsupported schemas, or a packet it did not actually review.

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

A catalog scenario/shard/release gate MAY declare visual review required. For
such a gate, missing, invalid, tampered, or `REVIEW_UNAVAILABLE` visual review
SHALL remain non-pass. Scenarios that do not declare visual review required MAY
retain images/review as diagnostic evidence without changing their ordinary
outcome.

#### Scenario: Required review is unavailable

- **WHEN** a presentation-sensitive gate declares visual review required but no
  capable reviewer is available
- **THEN** the gate records the missing review explicitly and SHALL NOT claim
  full visual qualification

#### Scenario: Optional review is unavailable

- **WHEN** a scenario collects visual evidence for diagnosis but its release
  contract does not require a visual review
- **THEN** `REVIEW_UNAVAILABLE` is recorded informationally and ordinary
  qualification semantics remain intact

### Requirement: Visual-review acceptance SHALL be demonstrated with non-vacuous controlled fixtures

The visual-review infrastructure SHALL be validated using test-owned seeded
healthy and defective packets whose answer is not encoded trivially in file
names or reviewer instructions. At minimum, acceptance SHALL include one
healthy packet and one concrete seeded visual defect. CI SHALL verify packet,
schema, hashing, and known result fixtures without requiring a live multimodal
model.

#### Scenario: Seeded occlusion packet is reviewed

- **WHEN** a capable agent reviews a controlled packet containing a deliberately
  occluded guest presentation without being told which image is defective
- **THEN** it identifies the visual problem and emits a valid hash-bound
  `VISUAL_DEFECT` result

#### Scenario: Seeded healthy packet is reviewed

- **WHEN** the same workflow reviews a controlled healthy presentation packet
- **THEN** it does not fabricate a defect and can emit a valid
  `VISUAL_OK` result subject to the normal native-evidence boundary

