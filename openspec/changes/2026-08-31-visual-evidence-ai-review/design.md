# Design — retained visual evidence and multimodal agent review

## 1. Problem statement

ValidationDriver already has two useful capture primitives:

- `Pixels.CaptureHostScreenArea(HWND)` captures the DWM-composited screen
  pixels over a window client area with `BitBlt`.
- `Pixels.CaptureWindowViaPrintWindow(HWND)` asks a window to render into an
  off-screen surface with `PrintWindow(PW_RENDERFULLCONTENT)`.

Those functions currently return raw `int[]` pixels primarily for numeric
assertions such as brightness, frame difference, and dominant color. The image
itself normally does not survive the assertion.

The architecture therefore has three distinct questions today:

1. **Native truth** — which HWND/process/monitor/rect/foreground/z-order state
   exists?
2. **Metric truth** — is the sampled region bright, changing, or dominated by
   the expected controlled color?
3. **Human-visible truth** — what was actually drawn and composed on screen?

Questions 1 and 2 are already strong. This change makes question 3 durable and
reviewable.

The design must preserve TabDock's current strengths: fail-closed physical
input, exact candidate identity, first-attempt authority, bounded artifacts,
privacy-safe support evidence, Shepherd/no-reparent semantics, and explicit
synthetic-versus-physical classification.

## 2. Architecture overview

The implementation SHALL be split into six layers so screenshot capture does
not become entangled with scenario logic or model APIs.

### Layer A — capture primitives

Owns acquisition of pixels and image dimensions only.

Candidate classes:

- `VisualCapture`
- `VisualFrame`
- `VisualCaptureScope`

Responsibilities:

- capture DWM-composited target/client regions;
- capture direct window rendering where useful;
- capture a bounded context rectangle around an approved target;
- return width, height, screen rect, acquisition method, timestamp and raw
  pixels;
- never decide scenario outcome;
- never call an AI service;
- never send input.

The existing `Pixels` metric functions may remain where they are or be split
into `PixelMetrics`; avoid a broad rewrite merely for naming.

### Layer B — evidence recorder

Owns checkpoint policy, encoding, retention limits, file naming, hashing and
manifest registration.

Candidate classes:

- `VisualEvidenceRecorder`
- `VisualCheckpointRequest`
- `VisualArtifactRecord`
- `VisualEvidencePolicy`

Responsibilities:

- decide whether a requested checkpoint is allowed under the current policy;
- encode raw frames as PNG;
- compute SHA-256 over the exact retained bytes;
- write only below the run-owned artifact root;
- attach metadata to the current attempt/scenario;
- enforce artifact count/byte/dimension limits;
- fail closed or downgrade to an explicit evidence status when required capture
  cannot be produced.

### Layer C — bounded flight recorder

Owns ephemeral rolling visual context for short-lived defects.

Candidate class:

- `VisualRingBuffer`

Responsibilities:

- sample only while a scenario explicitly enables a high-risk transition;
- use a low fixed maximum frame rate;
- hold a bounded number of frames/bytes in memory;
- discard healthy rolling history by default;
- flush the last N frames plus the failure frame only when requested;
- stop in `finally`.

This is not a general screen recorder.

### Layer D — review packet builder

Transforms image artifacts plus existing machine evidence into a compact,
agent-readable packet.

Candidate classes:

- `VisualReviewPacketBuilder`
- `VisualReviewPacket`
- `VisualReviewCheckpoint`

Responsibilities:

- select the minimum relevant images;
- produce a contact sheet for sequence comprehension;
- link raw images without altering them;
- include expected visual invariants;
- include correlated UIA/native/pixel/timeline facts;
- produce `visual-review-manifest.json`;
- produce a concise generated `visual-review-instructions.md` or equivalent
  text artifact;
- never infer an AI verdict itself.

### Layer E — multimodal agent reviewer

This is a workflow contract, not a model SDK embedded in ValidationDriver.

A capable development agent:

1. reads `visual-review-manifest.json`;
2. opens the contact sheet;
3. opens raw frames needed for detail;
4. compares expected and observed presentation;
5. cross-checks native/UIA/pixel facts;
6. emits `visual-review-result.json`.

The agent may be Codex, Claude, Kimi, OpenCode, ChatGPT, or another harness that
can actually inspect local image files. The repository SHALL not assume a
specific provider.

### Layer F — verifier and qualification integration

Owns schema validation and release semantics.

Candidate classes/tools:

- `VisualReviewVerifier`
- extensions to run-manifest/bundle verification

Responsibilities:

- validate review schema;
- verify referenced checkpoint/image hashes;
- verify run/attempt/candidate binding;
- reject stale review output copied from another run;
- keep visual review separate from native outcome vocabulary;
- block a visual-review-required gate when the review is missing or invalid;
- never treat model confidence as a desktop lease or native identity proof.

## 3. Capture scopes

The recorder SHALL use explicit capture scopes. No API may silently expand a
window capture into an entire desktop capture.

Recommended scope enum:

- `HostClient` — DWM-composited client region of the TabDock content host;
- `ContainerWindow` — TabDock container bounding rectangle;
- `GuestWindow` — captured guest bounding rectangle when approved;
- `OwnedPopup` — bounded popup/menu/dialog rectangle;
- `TargetWithContext` — target rectangle inflated by a small configured margin
  and clipped to the relevant monitor work area;
- `VirtualDesktop` — disabled by default; explicit supervised diagnostics only.

Every artifact records:

- requested scope;
- actual capture rectangle;
- target HWND where applicable;
- root HWND and process-start identity where applicable;
- monitor identity;
- DPI;
- capture method;
- whether pixels are screen-composited or direct-window rendered;
- whether the scope may contain non-test-owned pixels;
- privacy classification.

For z-order/occlusion bugs, screen-composited capture is the authoritative
visual source because it records what the user actually sees. `PrintWindow`
is a useful secondary comparison: if direct-window rendering looks healthy while
the screen-composited image is covered or blank, that is strong evidence of a
composition/z-order problem rather than a guest rendering failure.

## 4. Checkpoint model

Scenarios SHALL request semantic checkpoints rather than arbitrary file names.

Recommended checkpoint phases:

- `Baseline`
- `BeforeAction`
- `AfterActionImmediate`
- `AfterActionSettled`
- `BeforeAssertion`
- `Suspicious`
- `AssertionFailure`
- `FinalHealthy`
- `BeforeCleanup`

Each request includes:

- stable checkpoint ID;
- action ID or assertion ID;
- human-readable expectation;
- one or more capture scopes;
- whether capture is required or best-effort;
- whether the ring buffer should flush;
- whether the checkpoint must enter the AI review packet.

Example conceptual API:

```csharp
ctx.Visual.Checkpoint(new VisualCheckpointRequest(
    Id: "rename-editor-open",
    Phase: VisualCheckpointPhase.AfterActionSettled,
    Expectation: "Rename editor centered; active guest remains visible below caption.",
    Required: true,
    Scopes:
    [
        VisualCaptureScope.ContainerWindow(container),
        VisualCaptureScope.HostClient(host),
    ]));
```

Scenario code should not know PNG paths, hash algorithms, or contact-sheet
layout.

## 5. Event-driven capture policy

Default physical scenarios SHALL not capture continuously.

The recommended policy is:

1. baseline after scenario setup is stable;
2. immediately before a presentation-sensitive action;
3. immediately after the action;
4. once after the existing bounded settle condition;
5. immediately on a failed visual/native assertion;
6. once before cleanup when final presentation matters.

This generally produces 3–8 retained images for a focused scenario rather than
hundreds.

A scenario may declare `VisualEvidenceLevel`:

- `None` — no retained images;
- `FailureOnly` — baseline plus failure/suspicion capture;
- `Checkpoints` — semantic checkpoints;
- `FlightRecorder` — checkpoints plus temporary bounded rolling frames.

The CLI should support an explicit override such as:

- `--visual-evidence none|failure|checkpoints|flight`
- `--visual-review-packet`
- `--visual-max-bytes N`

Exact flag names may be refined during implementation, but visual collection
must remain opt-in/bounded for contexts where privacy matters.

## 6. Flight recorder design

Transient bugs can occur and repair themselves before the settled checkpoint.
The flight recorder exists for these cases only.

Recommended initial bounds:

- maximum 2 frames/second;
- maximum 6 seconds of history;
- maximum 12 frames;
- one approved capture scope per recorder unless a scenario explicitly declares
  more;
- hard total byte/memory ceiling;
- no persistence unless a failure/suspicious event flushes the buffer.

Those values are defaults, not release requirements; the implementation must
expose them as bounded policy constants and tests must prove the limits.

The ring buffer stores `VisualFrame` objects in memory. On
`FlushForFailure(checkpointId)`, frames are encoded and retained with sequence
timestamps relative to the action/failure. Healthy scenarios may retain only
semantic checkpoints.

The buffer must terminate in `finally`, including exceptions, cancellation and
scenario timeout.

## 7. PNG encoding and artifact integrity

TabDock is Windows-only, so the ValidationDriver may use an existing framework
facility available to its target to encode 32-bit pixels as PNG. Avoid adding a
large third-party imaging dependency solely for encoding.

Implementation criteria:

- preserve dimensions exactly;
- define the pixel channel conversion explicitly from the current
  `0x00RRGGBB` representation;
- no lossy recompression for authoritative raw checkpoints;
- write through an atomic temp-file/rename pattern under the attempt artifact
  root;
- compute SHA-256 after final bytes are written;
- reject path traversal and absolute-path registrations;
- register MIME `image/png`;
- keep originals immutable once registered.

Suggested layout:

```text
<run>/
  scenarios/
    <scenario-id>/
      attempts/
        001/
          result.json
          timeline.json
          visual/
            visual-manifest.json
            checkpoints/
              001-baseline-container.png
              002-before-rename-container.png
              003-rename-open-container.png
              003-rename-open-host.png
              004-failure-container.png
            ring/
              004-failure-minus-1500ms.png
              004-failure-minus-1000ms.png
              ...
            review/
              contact-sheet.png
              visual-review-manifest.json
              visual-review-instructions.md
              visual-review-result.json
```

Generated files remain ignored/untracked.

## 8. Visual artifact schema

Each retained image SHALL have a structured record similar to:

```json
{
  "schema": "tabdock-visual-artifact-v1",
  "artifactId": "va:rename:attempt-1:003:container",
  "candidateCommit": "<sha>",
  "candidateExecutableSha256": "<sha256>",
  "runId": "<run-id>",
  "scenarioId": "rename",
  "attempt": 1,
  "checkpointId": "rename-editor-open",
  "phase": "AfterActionSettled",
  "capturedAtUtc": "2026-08-31T...",
  "relativePath": "visual/checkpoints/003-rename-open-container.png",
  "sha256": "<sha256>",
  "mime": "image/png",
  "width": 1200,
  "height": 780,
  "scope": "ContainerWindow",
  "captureMethod": "DwmScreenBitBlt",
  "screenRect": { "left": 100, "top": 100, "right": 1300, "bottom": 880 },
  "dpi": 120,
  "monitor": "<stable-run-local-monitor-id>",
  "targetIdentity": {
    "hwnd": "<run-local encoded value>",
    "processStartIdentity": "<run-local identity>"
  },
  "privacy": {
    "classification": "test-owned",
    "mayContainExternalPixels": false
  },
  "expectation": "Rename editor centered; guest remains visibly presented."
}
```

Exact field naming must align with existing manifest conventions and privacy
rules. Do not expose raw machine paths in portable bundles.

## 9. Contact sheet design

AI agents often understand a short visual sequence better from one overview
image and then drill into originals.

The packet builder SHALL optionally create a contact sheet containing:

- chronological thumbnails;
- checkpoint ID;
- phase;
- relative time;
- capture scope;
- a short expectation label.

The contact sheet is a derived convenience artifact. The raw PNG remains the
authoritative visual evidence and its hash must be referenced separately.

Do not paint native rectangles or labels over the authoritative raw image.
If annotated images are desired, write separate derived copies and clearly mark
them `derived=true`.

## 10. AI visual review contract

### 10.1 Why the agent can actually "look"

A multimodal coding agent that has an image-view/open-image capability can
inspect the generated PNG files just as it can inspect a user-provided
screenshot. The repository does not need to convert an image into text first.

The missing engineering piece is therefore not "teach TabDock computer vision."
It is:

- retain the image;
- make its local path discoverable;
- describe what the frame is expected to show;
- give the agent enough structured context to reason about it;
- require a structured answer tied to the exact image.

Some headless agents do not have image input. Those agents report
`REVIEW_UNAVAILABLE`; they must not fabricate visual review.

### 10.2 Review packet

Recommended `visual-review-manifest.json` fields:

- schema;
- candidate/run/scenario/attempt identity;
- packet hash/version;
- ordered checkpoints;
- raw image path + SHA-256;
- optional contact-sheet path + SHA-256;
- expected visual invariants;
- prior checkpoint relationship;
- relevant native/UIA/pixel facts;
- timeline offsets;
- known environment variations;
- prohibited inference reminders;
- required output path/schema.

Do not dump the entire run log into the packet. Include only bounded correlated
facts and links to the full evidence.

### 10.3 Reviewer instructions

The generated instructions and repository workflow SHALL tell the agent to:

1. verify it can open the contact sheet/raw images;
2. inspect the sequence chronologically;
3. identify visible symptoms, not presumed causes;
4. compare each required checkpoint with its expectation;
5. inspect originals for any suspicious thumbnail;
6. correlate with native/UIA/pixel evidence;
7. distinguish presentation defect from capture artifact;
8. record uncertainty;
9. never infer process identity or desktop safety from pixels;
10. never modify production code until a defect is classified.

### 10.4 Review vocabulary

Use a visual-specific vocabulary separate from scenario outcomes:

- `VISUAL_OK` — reviewed evidence matches declared visual expectations;
- `VISUAL_SUSPECT` — something appears wrong/ambiguous and needs deeper
  correlation;
- `VISUAL_DEFECT` — reviewed images show a concrete visual contract violation;
- `REVIEW_UNAVAILABLE` — no capable reviewer or image could not be opened/
  validated.

Optional per-finding categories:

- `OCCLUSION`
- `BLANK_OR_BLACK_REGION`
- `WRONG_GUEST_PRESENTED`
- `CLIPPING`
- `MISALIGNMENT`
- `POPUP_PLACEMENT`
- `Z_ORDER_COMPOSITION`
- `TRANSIENT_FLICKER_OR_FLASH`
- `STALE_FRAME`
- `GEOMETRY_DRIFT_VISIBLE`
- `DPI_SCALING_ANOMALY`
- `UNEXPECTED_CHROME`
- `CAPTURE_ARTIFACT`
- `OTHER`

### 10.5 Review result schema

Conceptual output:

```json
{
  "schema": "tabdock-visual-review-result-v1",
  "packetSha256": "<packet hash>",
  "candidateCommit": "<sha>",
  "runId": "<run-id>",
  "scenarioId": "rename",
  "attempt": 1,
  "reviewer": {
    "kind": "multimodal-agent",
    "harness": "codex",
    "model": "optional informational string"
  },
  "verdict": "VISUAL_DEFECT",
  "confidence": 0.94,
  "findings": [
    {
      "findingId": "vf-001",
      "category": "OCCLUSION",
      "checkpointIds": ["rename-editor-open"],
      "artifactIds": ["va:rename:attempt-1:003:container"],
      "observation": "Opaque container region covers the expected guest content.",
      "expected": "Guest remains visibly presented below the caption.",
      "severity": "high",
      "regionNormalized": { "x": 0.08, "y": 0.14, "w": 0.84, "h": 0.73 },
      "correlation": {
        "nativeEvidenceConsistent": false,
        "notes": "Geometry reports guest aligned, but composited pixels contradict presentation health."
      }
    }
  ],
  "uncertainties": [],
  "reviewedArtifactSha256": ["..."]
}
```

Do not require a free-form chain-of-thought field. The result should contain
concise observable rationale and evidence references only.

## 11. Qualification semantics

AI review SHALL not become a new source of unsafe authority.

Rules:

1. Native lease/identity/ownership failures remain authoritative and can block
   before visual review.
2. `VISUAL_OK` does not repair a failed native invariant.
3. `VISUAL_DEFECT` can prevent a visual-review-required scenario from being
   accepted as visually qualified.
4. `VISUAL_DEFECT` does not automatically prove which component caused the
   defect.
5. A production `FAIL_PRODUCT` classification still requires valid scenario
   prerequisites and normal evidence correlation.
6. `REVIEW_UNAVAILABLE` is non-pass for a gate that explicitly requires AI/
   human visual review, but may be informational for ordinary CI.
7. Missing/tampered image evidence is `FAIL_HARNESS` when the scenario declared
   the image required.
8. An AI review whose packet/image hashes do not match is invalid evidence.
9. The first valid visual defect remains retained across reruns just like other
   first-attempt evidence.

A future release policy may require visual review only for selected
presentation-sensitive shards rather than every scenario.

## 12. Agent-facing workflow

Implementation SHALL add one canonical repository workflow, for example:

`.agent/workflows/visual-evidence-review.md`

The workflow should be harness-neutral and say, in effect:

1. locate the newest or explicitly supplied review packet;
2. validate packet/image hashes using the provided verifier;
3. open contact sheet with the harness's image-view capability;
4. open raw suspicious checkpoints at full resolution;
5. write `visual-review-result.json` using the schema;
6. run the verifier;
7. if `VISUAL_DEFECT` or `VISUAL_SUSPECT`, inspect correlated timeline/native
   evidence and source before editing;
8. preserve the packet/result in the run artifacts;
9. summarize findings with exact checkpoint IDs.

Harness adapters may point to this workflow. Do not duplicate the protocol into
multiple provider-specific files unless generated through the existing agent
configuration sync mechanism.

## 13. Optional automated model adapter

A later implementation MAY provide a separate, opt-in adapter that sends review
packets to a configured multimodal API. This is explicitly not required for the
first version and should not be built until the agent-driven workflow works.

If added later:

- credentials come from environment/secret storage, never repo files;
- no provider is mandatory;
- image upload is explicit;
- privacy classification must allow external inference;
- remote review result uses the same schema;
- model/provider/version are recorded;
- failures return `REVIEW_UNAVAILABLE`, never an invented review.

The recommended first release is **agent-mediated local review**, because the
development agent already has the source context and can often use its own
vision capability without TabDock managing another inference stack.

## 14. Controlled visual baselines

Exact screenshot comparison is unsuitable as a universal oracle because of:

- Windows version differences;
- DWM composition;
- GPU/driver differences;
- ClearType/font rasterization;
- WPF rendering differences;
- theme/high-contrast settings;
- DPI rounding;
- real application chrome/content.

For controlled GuineaPig fixtures, the system MAY use stable region-based
metrics or tolerant perceptual comparison as a secondary deterministic signal.

Good candidates:

- expected dominant-color pane exists in assigned region;
- known opaque container color does not cover guest region;
- title/editor midpoint measured through UIA and optionally image edge/region
  sanity;
- two split colors occupy approximately expected halves;
- popup bounding region is visible near the invoking control.

Any baseline algorithm must define tolerance, resize/DPI normalization and
capture method. Do not introduce a magical "similarity > 0.98 == PASS" rule
without fixture-specific evidence.

## 15. Scenario rollout

### Wave 1 — controlled fixtures

Integrate visual evidence into:

- `tabswitch-hidesafety`
- `minrestore`
- `maximize-repro`
- `guest-maximize-contained`
- one split scenario
- one context-menu/chrome scenario

Use GuineaPig colors/pulse behavior to seed known-good and known-bad visual
conditions.

### Wave 2 — presentation-integrity scenarios

Add semantic checkpoints to:

- rename;
- workspace/group menu;
- split enter/focus/end;
- inline capture `+` surface;
- context menu;
- chrome-click rendering;
- title centering;
- topmost fixture.

### Wave 3 — real app/topology

Once privacy and capture scoping are proven:

- browser F11;
- dual-monitor transfer;
- mixed-DPI movement;
- adopted real apps.

Real-app imagery should be minimized/cropped and must not become default CI
evidence.

## 16. Seeded defect validation

The implementation needs proof that the AI review path is not ceremonial.

Create test-owned visual fixtures or driver self-test inputs containing
deliberate conditions such as:

- opaque rectangle covering the guest region;
- title visibly offset from center;
- wrong colored guest shown;
- popup clipped outside expected bounds;
- split divider/regions visibly wrong.

The agent review acceptance campaign SHALL include:

- at least one seeded healthy packet;
- at least one seeded defect packet;
- one ambiguous/capture-artifact packet if practical.

The reviewer should correctly flag the defect without being given the answer in
the file name or prompt. Keep these fixtures synthetic/test-owned; do not alter
production TabDock to create defects.

## 17. Privacy controls

Visual evidence requires stricter controls than existing logs.

Required controls:

- visual mode disabled or minimized by default for CI;
- test-owned scopes preferred;
- no whole-desktop capture by default;
- real-app capture explicitly labeled;
- no image artifact included in support bundles unless separately authorized;
- no generated images committed;
- artifact retention bounded by count and bytes;
- portable paths only;
- review packet contains no absolute local paths;
- optional external-model review requires an explicit privacy policy gate;
- packet verifier rejects unexpected files or path escape.

Consider a `privacyClass` enum:

- `TestOwned`
- `ProductOwned`
- `RealAppRestricted`
- `DesktopRestricted`

Only the first two should be eligible for routine agent review without an
explicit operator decision.

## 18. Performance and resource constraints

Image capture/encoding must not turn the validation harness into the defect.

Measure:

- capture duration;
- PNG encode duration;
- bytes per artifact;
- ring-buffer memory;
- total retained bytes;
- dropped/skipped frames;
- scenario runtime delta.

The recorder should expose counters in the result artifact.

Hard invariants:

- no unbounded queue;
- no background encoder surviving scenario cleanup;
- no capture loop after cancellation;
- no artifact write outside run root;
- no image retention after policy limit is reached;
- failures to encode required evidence are explicit.

## 19. Testing strategy

### Unit tests

Test:

- pixel-to-PNG channel correctness;
- dimensions/stride;
- path normalization;
- artifact IDs/file names;
- SHA-256 registration;
- byte/count limits;
- ring-buffer eviction;
- ring flush ordering;
- checkpoint selection;
- privacy scope admission;
- packet selection;
- review schema validation;
- hash mismatch rejection;
- stale run/candidate rejection;
- missing review behavior;
- `VISUAL_*` aggregation semantics.

### Driver self-tests

Use generated in-memory images/fixtures to validate:

- capture artifact manifests;
- contact-sheet generation;
- packet determinism;
- offline verification;
- seeded visual defect packets;
- no desktop input required.

### Physical tests

On an exclusive supervised desktop:

- confirm screen-composited PNG matches what the operator sees;
- compare BitBlt and PrintWindow on controlled and GPU-rendered guests;
- trigger a known controlled occlusion fixture and retain evidence;
- exercise flight-recorder flush;
- verify runtime/resource limits.

## 20. CI strategy

CI must not require a real desktop or AI model.

CI SHALL:

- build the capture/review code;
- unit-test encoding/manifests/verifier;
- create synthetic PNG fixtures;
- build review packets deterministically;
- verify seeded review-result fixtures;
- verify tamper/missing/stale negative cases;
- ensure strict OpenSpec validation remains green.

CI MAY retain synthetic visual artifacts as workflow evidence if bounded.

Actual multimodal model review remains local/supervised/optional unless a future
separate policy explicitly introduces a hosted vision reviewer.

## 21. Interaction with presentation-certification evidence

The `2026-08-31-presentation-integrity-physical-certification` campaign has
completed its exercised matrix. Its physical artifacts remain authoritative for
the exact candidate, run, and attempt that produced them; the final mainline
qualification separately proves the current source identity and deterministic
gates.

This future infrastructure is not required to retroactively justify valid
physical PASS evidence, and it SHALL not relabel a retained
`BLOCKED_ENVIRONMENT`, `BLOCKED_CAPABILITY`, `SKIP_CAPABILITY`, or historical
failure. It SHALL preserve the campaign's lease, candidate/process/HWND,
foreground, `WindowFromPoint`/`GA_ROOT`, geometry, DPI, z-order, cleanup, and
native-assertion boundaries.

Future physical campaigns MAY consume visual packets as supplemental evidence
when their explicit privacy and retention policy permits it. A visual result
never replaces hard evidence or converts a blocked physical cell into PASS.

## 22. Migration and schema strategy

Introduce new visual schemas rather than silently changing old evidence meaning.

Recommended generations:

- `tabdock-visual-artifact-v1`
- `tabdock-visual-review-packet-v1`
- `tabdock-visual-review-result-v1`

Extend run/bundle schemas only where necessary to index those artifacts.
Offline verifiers should explicitly support old runs that have no visual
artifacts and new runs that declare visual evidence.

Do not require historical bundles to fabricate visual records.

## 23. Failure taxonomy

Examples:

- capture API fails for optional checkpoint -> record visual capture warning/
  unavailable metadata; scenario native outcome unchanged;
- capture API fails for required checkpoint -> `FAIL_HARNESS`;
- PNG written but missing/hash mismatch -> `FAIL_HARNESS`;
- contact sheet fails but raw images valid -> review packet may remain valid if
  contact sheet is optional;
- multimodal reviewer unavailable -> `REVIEW_UNAVAILABLE`;
- AI says defect but image hash is stale -> reject review;
- AI says defect and native preconditions were invalid -> visual finding retained,
  but do not classify product failure;
- AI says defect with valid prerequisites -> block visual acceptance and begin
  normal defect investigation;
- AI says OK but hard native assertion failed -> scenario remains failed;
- AI and human/native evidence disagree -> `VISUAL_SUSPECT`/unresolved, do not
  average them into PASS.

## 24. Completion criteria

Implementation is complete only when:

1. capture, retention, hash/index, packet and verifier layers are separated;
2. event-driven checkpoints work in controlled scenarios;
3. failure flight recorder is bounded and proven to stop/evict correctly;
4. raw screenshots are immutable authoritative artifacts;
5. review packets correlate images with native/UIA/timeline facts;
6. repository agent workflow tells a multimodal agent how to actually open and
   review images;
7. a capable agent can produce a valid review result without a vendor SDK in
   TabDock;
8. missing/non-vision agents fail honestly as `REVIEW_UNAVAILABLE`;
9. seeded visual defects demonstrate non-vacuous detection;
10. visual review cannot override lease/identity/native safety failures;
11. privacy/performance limits are enforced and tested;
12. old non-visual evidence remains verifiable;
13. docs and active physical-certification integration guidance are updated;
14. Debug/Release deterministic gates and strict OpenSpec validation pass.
