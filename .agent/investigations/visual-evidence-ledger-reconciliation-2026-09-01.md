# Predecessor visual-evidence ledger reconciliation

**Date:** 2026-09-01  
**Change:** `2026-08-31-visual-evidence-ai-review`  
**Canonical ledger:** `openspec/changes/2026-08-31-visual-evidence-ai-review/tasks.md`

## Authority and method

The repository-local OpenSpec CLI reports **178 total / 0 complete / 178
remaining** before this reconciliation. Git reports `HEAD == origin/main ==
68e456ff8500750c32474927ce724292a54f1245`, branch `main`, and a dirty
worktree. The current source, tests, documentation, workflow, and generated
run roots were inspected before changing the ledger. The current visual unit
filter passes **44/44** tests, while the Release build reports 16 nullable
warnings in the new visual path; those warnings are evidence against treating
strict collection/verifier acceptance as complete.

Classification is evidence-based:

- `COMPLETED_AND_PROVEN` means the current implementation is present and is
  directly exercised by deterministic tests, an inspected qualification
  artifact, or both. Only these rows are checked in `tasks.md`.
- `IMPLEMENTED_BUT_NOT_ACCEPTED` means a source or partial test exists, but
  the requested integration, coverage, offline gate, or acceptance evidence is
  incomplete.
- `NOT_IMPLEMENTED` means no current implementation or qualifying test exists.
- `SUPERSEDED` means the conditional/optional work was deliberately excluded by
  the current design decision; it is not represented as a completed feature.
- `BLOCKED_SUPERVISED` means the missing proof requires an exclusive supervised
  desktop, exact candidate run, operator acceptance, or archive boundary.
- `BLOCKED_CAPABILITY` means the missing proof requires a capable multimodal
  reviewer that is not represented by current repository evidence.

The task IDs below map one-to-one to the canonical task lines; no task was
silently omitted or collapsed.

## Dirty visual-file inventory before staging

| Path/group | Current state | Classification | Disposition |
| --- | --- | --- | --- |
| `scripts/qualification-bundle.ps1` | modified | intended visual bundle-verifier implementation | preserve; no staging performed |
| `tests/UnitTests/TabDock.UnitTests.csproj` | modified | intended pure visual-source/test linkage | preserve; no staging performed |
| `tests/ValidationDriver/TabDock.ValidationDriver/Pixels.cs` | modified | intended capture compatibility/metadata implementation | preserve; no staging performed |
| `tests/ValidationDriver/TabDock.ValidationDriver/Program.cs` | modified | intended visual CLI/policy implementation | preserve; no staging performed |
| `tests/ValidationDriver/TabDock.ValidationDriver/QualificationResultWriter.cs` | modified | intended visual/run-manifest integration | preserve; no staging performed |
| `tests/ValidationDriver/TabDock.ValidationDriver/Scenarios.Browser.cs` | modified | intended validation-fixture source change | preserve; no staging performed |
| `tests/ValidationDriver/TabDock.ValidationDriver/Scenarios.cs` | modified | intended recorder/context lifecycle integration | preserve; no staging performed |
| `tests/UnitTests/Visual*.cs` (10 files) | untracked | intended deterministic visual tests | preserve; no staging performed |
| `tests/ValidationDriver/TabDock.ValidationDriver/Visual*.cs` (11 files) | untracked | intended visual implementation | preserve; no staging performed |
| `.agent/investigations/visual-evidence-implementation-2026-09-01.md` | untracked | intended implementation checkpoint | preserve; no staging performed |
| `.agent/workflows/visual-evidence-review.md` | untracked | intended canonical agent workflow | preserve; no staging performed |
| `.visual-validation-runs/**` | untracked | generated run manifests/results; no visual manifest or PNG was found in the inspected roots | preserve on disk; exclude from Git and add an explicit ignore rule during closure |

No production TabDock/Shepherd source is modified. The generated run roots are
not implementation files and must not be used as proof of visual acceptance.

## Per-task classification

### 0. Orientation and scope lock

| ID | Classification | Evidence |
| --- | --- | --- |
| 0.1 | COMPLETED_AND_PROVEN | Current Git authority was resolved dynamically with `git status`, `git diff`, `git ls-files --others`, `git rev-parse HEAD`, branch, and `origin/main`. |
| 0.2 | COMPLETED_AND_PROVEN | `AGENTS.md`, `.agent/STATE.md`, `docs/TESTING.md`, archived presentation-certification artifacts, canonical qualification specs, and current ValidationDriver visual/source files were read. |
| 0.3 | COMPLETED_AND_PROVEN | Current `Pixels` callers, visual writers, run/result manifests, bundle verifier, timeline wiring, and `RunScenario` cleanup were inventoried in source and search results. |
| 0.4 | COMPLETED_AND_PROVEN | The dirty inventory contains only validation/evidence infrastructure plus planning artifacts; no production TabDock file is modified. |
| 0.5 | COMPLETED_AND_PROVEN | `.agent/investigations/visual-evidence-implementation-2026-09-01.md` exists and is linked by `.agent/STATE.md`; this reconciliation is the successor checkpoint. |

### 1. Current-capture baseline

| ID | Classification | Evidence |
| --- | --- | --- |
| 1.1 | COMPLETED_AND_PROVEN | The implementation checkpoint records controlled GuineaPig host and browser captures, including dimensions and measured latency. |
| 1.2 | COMPLETED_AND_PROVEN | `Pixels` documents and preserves `0x00RRGGBB` channel meaning, physical client-to-screen translation, BitBlt/PrintWindow methods, dimensions, and null failure behavior; `VisualPixelsRegressionTests` passes. |
| 1.3 | COMPLETED_AND_PROVEN | The checkpoint records 15–46 ms host capture, 14–27 ms PrintWindow capture, representative dimensions, and raw 4-byte memory sizes. |
| 1.4 | COMPLETED_AND_PROVEN | Existing scenario callers and brightness/frame-diff/dominant-channel use were searched; compatibility projections remain in `Pixels`; regression tests pass. |
| 1.5 | COMPLETED_AND_PROVEN | `VisualPixelsRegressionTests` directly covers channel metrics, frame diff, empty results, and invalid handles; the visual filter passed 44/44. |

### 2. Visual evidence domain model

| ID | Classification | Evidence |
| --- | --- | --- |
| 2.1 | COMPLETED_AND_PROVEN | `VisualEvidenceModel.cs` defines versioned `VisualFrame`, `VisualCaptureScope`, `VisualCheckpointPhase`, `VisualCheckpointRequest`, `VisualArtifactRecord`, and `VisualEvidencePolicy`; model tests pass. |
| 2.2 | COMPLETED_AND_PROVEN | Stable capture, scope, privacy, evidence-level, phase, requiredness, verdict, and finding enums are present and serialized with string enum names. |
| 2.3 | COMPLETED_AND_PROVEN | `VisualPathPolicy` rejects rooted, drive, UNC, empty, dot, and traversal segments and resolves only below the run root; path tests pass. |
| 2.4 | COMPLETED_AND_PROVEN | `VisualTargetIdentityFactory` projects existing `WindowIdentity` process/HWND/thread/start/ownership data and `VisualCaptureService` validates it before and after capture. |
| 2.5 | COMPLETED_AND_PROVEN | Model tests cover serialization, invalid enum text, malformed rectangles, empty dimensions, mismatched pixels, and unsafe paths. |

### 3. PNG encoding and immutable artifacts

| ID | Classification | Evidence |
| --- | --- | --- |
| 3.1 | COMPLETED_AND_PROVEN | `VisualPngEncoder` implements bounded RGBA8 lossless PNG encoding without a new imaging dependency. |
| 3.2 | COMPLETED_AND_PROVEN | PNG tests prove red/green/blue byte ordering and exact dimensions. |
| 3.3 | COMPLETED_AND_PROVEN | `VisualArtifactStore` writes same-directory bounded temporary files, flushes, and atomically moves them; rename-failure cleanup is tested. |
| 3.4 | COMPLETED_AND_PROVEN | Final bytes are SHA-256 hashed and returned with size; `VisualArtifactRecord` stores MIME, hash, size, and dimensions; tests verify the hash. |
| 3.5 | COMPLETED_AND_PROVEN | Path policy and artifact-store code reject absolute/traversal/outside-root paths and duplicate IDs/paths; tests cover unsafe paths and duplicates. |
| 3.6 | COMPLETED_AND_PROVEN | Raw artifacts are registered separately from derived contact sheets; the contact-sheet test proves raw bytes/hash remain unchanged. |
| 3.7 | COMPLETED_AND_PROVEN | Store exception cleanup removes temporary/final partial files; the forced atomic-rename failure test proves no `.tmp` residue. |

### 4. Capture-scope implementation

| ID | Classification | Evidence |
| --- | --- | --- |
| 4.1 | COMPLETED_AND_PROVEN | `HOST_CLIENT` resolves to the client boundary and `VisualCaptureService` acquires it through `Pixels.CaptureScreenRectDetailed`; controlled host capture characterization and source guards exist. |
| 4.2 | IMPLEMENTED_BUT_NOT_ACCEPTED | Container-window screen composition is represented by the bounded window scope, but no current visual-enabled scenario artifact proves the container path end to end. |
| 4.3 | IMPLEMENTED_BUT_NOT_ACCEPTED | Guest capture and before/after strong identity validation are implemented, but no current visual packet from an integrated guest scenario proves the native path. |
| 4.4 | NOT_IMPLEMENTED | No dedicated known TabDock-owned popup/dialog HWND admission or scenario integration exists; the generic window resolver is not proof of the requested popup scope. |
| 4.5 | COMPLETED_AND_PROVEN | `TARGET_WITH_CONTEXT` inflates by a bounded margin, clips to monitor work area, and is directly tested with mixed/ clipped geometry. |
| 4.6 | COMPLETED_AND_PROVEN | Virtual-desktop capture requires both policy and scope authorization; defaults disable it and tests reject unauthorized construction. |
| 4.7 | COMPLETED_AND_PROVEN | Frame/artifact records contain requested/actual rectangles, monitor, DPI, method, target identity, privacy, and capture duration; recorder tests bind metadata. |
| 4.8 | IMPLEMENTED_BUT_NOT_ACCEPTED | Geometry tests cover clipping, narrow/empty intersections, and recycled identity, but do not cover the full required negative/above-origin, zero-size native, stale-HWND, and DPI matrix through fakes. |

### 5. Recorder and checkpoint API

| ID | Classification | Evidence |
| --- | --- | --- |
| 5.1 | COMPLETED_AND_PROVEN | `Ctx.VisualCheckpoint` is the scenario-facing semantic API; recorder tests use requests rather than file paths. |
| 5.2 | COMPLETED_AND_PROVEN | All nine requested checkpoint phases are defined and policy-gated. |
| 5.3 | COMPLETED_AND_PROVEN | Checkpoint validation requires stable IDs and bounded non-empty expectations; retained artifacts carry both. |
| 5.4 | COMPLETED_AND_PROVEN | One request accepts multiple scopes and the recorder test captures two artifacts from one request. |
| 5.5 | COMPLETED_AND_PROVEN | Requiredness is explicit in requests/results and required versus best-effort failure behavior is tested. |
| 5.6 | COMPLETED_AND_PROVEN | Required recorder failures produce `RequiredFailure`; `Ctx.VisualCheckpoint` maps them to `FAIL_HARNESS`; recorder tests prove the failure boundary. |
| 5.7 | COMPLETED_AND_PROVEN | Optional failures produce `VisualUnavailableRecord` entries without changing the recorder's native outcome; tests cover optional and required failures separately. |
| 5.8 | COMPLETED_AND_PROVEN | Artifact count and byte budgets are enforced before finalization; the hard-budget test proves no optional artifact is accepted after exhaustion. |
| 5.9 | COMPLETED_AND_PROVEN | Capture duration is carried from `PixelCaptureResult` to `VisualFrame`/artifact, and counters expose encode time and retained bytes. |

### 6. CLI and policy controls

| ID | Classification | Evidence |
| --- | --- | --- |
| 6.1 | COMPLETED_AND_PROVEN | `Program` parses `none`, `failure`, `checkpoints`, and `flight`; `VisualPolicyResolver` centralizes policy defaults. |
| 6.2 | COMPLETED_AND_PROVEN | `--visual-review-packet` is parsed and propagated to child shards; policy carries the switch. |
| 6.3 | COMPLETED_AND_PROVEN | CLI byte bounds and policy artifact/dimension/ring bounds are finite and validated; policy tests prove bounded defaults. |
| 6.4 | IMPLEMENTED_BUT_NOT_ACCEPTED | Default/ordinary CI invocation uses disabled visual policy, but no explicit CI-mode guard or CI regression assertion proves that a future flag cannot initiate desktop capture. |
| 6.5 | COMPLETED_AND_PROVEN | Virtual desktop is disabled and every window capture is declared/validated against an approved target scope; no policy path silently widens to the desktop. |
| 6.6 | COMPLETED_AND_PROVEN | Scenario and run manifests serialize the effective visual policy and visual artifact links in `QualificationResultWriter`. |

### 7. Automatic failure capture

| ID | Classification | Evidence |
| --- | --- | --- |
| 7.1 | NOT_IMPLEMENTED | `Ctx.Check`/exception handling never automatically requests a final visual checkpoint before cleanup. |
| 7.2 | NOT_IMPLEMENTED | Because the assertion/error hook is absent, there is no implemented recursive-failure guard for capture during assertion handling. |
| 7.3 | IMPLEMENTED_BUT_NOT_ACCEPTED | `CaptureFailure` embeds a failure reason in the expectation, but no integrated assertion/action trigger identity is recorded. |
| 7.4 | COMPLETED_AND_PROVEN | Attempt-numbered artifact paths preserve first and rerun failure images; the recorder test proves attempt-001 and attempt-002 coexist. |
| 7.5 | IMPLEMENTED_BUT_NOT_ACCEPTED | Recorder flush has a `finally` stop and failure cleanup tests exist, but no integrated thrown/timeout assertion path proves scenario cleanup under automatic failure capture. |

### 8. Bounded visual flight recorder

| ID | Classification | Evidence |
| --- | --- | --- |
| 8.1 | COMPLETED_AND_PROVEN | `VisualRingBuffer` enforces frame count, bytes, duration, and frame rate; ring tests prove eviction and bounds. |
| 8.2 | COMPLETED_AND_PROVEN | Safe defaults centralize 2 fps, 6-second, 12-frame, and 8 MiB ring limits; policy/ring tests exercise bounded defaults. |
| 8.3 | NOT_IMPLEMENTED | No scenario marks a high-risk transition and starts/stops the recorder around that transition. |
| 8.4 | COMPLETED_AND_PROVEN | Healthy `Stop` clears in-memory history; ring tests prove healthy discard and stopped state. |
| 8.5 | COMPLETED_AND_PROVEN | Failure flush captures a forced trigger and ordered pre-trigger frames into immutable ring artifacts; ring and recorder tests prove ordering/flush. |
| 8.6 | COMPLETED_AND_PROVEN | Ring frames receive sequence and relative milliseconds; tests prove 500/1000/1500 ms ordering. |
| 8.7 | IMPLEMENTED_BUT_NOT_ACCEPTED | `FlushFlightRecorder`, `RunScenario` finally, and `Dispose` stop recorder work, but cancellation, timeout, process-exit, and abort paths lack direct integrated proof. |
| 8.8 | IMPLEMENTED_BUT_NOT_ACCEPTED | Deterministic eviction/order and healthy-discard tests exist; cancellation and explicit memory-limit failure coverage is incomplete. |
| 8.9 | NOT_IMPLEMENTED | No measured recorder-overhead report or documented observed budget exists. |

### 9. Visual manifest integration

| ID | Classification | Evidence |
| --- | --- | --- |
| 9.1 | COMPLETED_AND_PROVEN | Recorder and `QualificationResultWriter` create one attempt/scenario `manifest.json` with artifact records; manifest tests pass. |
| 9.2 | IMPLEMENTED_BUT_NOT_ACCEPTED | Result/run-manifest links are implemented, but inspected current run roots contain no visual manifest proving the hierarchy end to end. |
| 9.3 | IMPLEMENTED_BUT_NOT_ACCEPTED | The C# artifact index and PowerShell visual verifier enumerate visual artifacts, but the C# index omits review packet/instruction files, so a packet-enabled bundle is not yet proven accepted. |
| 9.4 | IMPLEMENTED_BUT_NOT_ACCEPTED | PowerShell and C# paths reject many missing/modified/duplicate/path/stale/schema cases, but strict collection and computed-packet-hash gaps remain. |
| 9.5 | IMPLEMENTED_BUT_NOT_ACCEPTED | Non-visual paths return without a visual section and existing historical manifests remain readable, but no dedicated visual-absence compatibility test is present. |
| 9.6 | IMPLEMENTED_BUT_NOT_ACCEPTED | Image tamper and stale candidate tests exist; metadata-hash, relative-path, packet, and scenario-binding mutations are not all covered deterministically. |

### 10. Contact sheet and derived evidence

| ID | Classification | Evidence |
| --- | --- | --- |
| 10.1 | COMPLETED_AND_PROVEN | `VisualContactSheetBuilder` orders selected raw artifacts chronologically and produces bounded PNG output; contact-sheet tests pass. |
| 10.2 | COMPLETED_AND_PROVEN | Labels include checkpoint ID, phase, relative time, scope, and trimmed expectation outside thumbnails; source and mixed-dimension tests prove bounds. |
| 10.3 | COMPLETED_AND_PROVEN | Contact sheets are `Derived` with `SourceArtifactId`; raw hashes remain separate and are tested unchanged. |
| 10.4 | COMPLETED_AND_PROVEN | Builder reads raw files and writes a separate derived PNG; raw-preservation tests prove no mutation. |
| 10.5 | IMPLEMENTED_BUT_NOT_ACCEPTED | Zero/one/many, mixed dimensions, and long labels are tested; packet-byte-limit coverage is missing. |
| 10.6 | IMPLEMENTED_BUT_NOT_ACCEPTED | Raw evidence survives contact-sheet failure and an unavailable record is emitted, but the record is not yet an authoritative schema-versioned derived-failure object propagated through every required consumer. |

### 11. AI visual-review packet

| ID | Classification | Evidence |
| --- | --- | --- |
| 11.1 | COMPLETED_AND_PROVEN | `VisualReviewPacket` uses `tabdock-visual-review-packet-v1`; packet tests validate the schema. |
| 11.2 | IMPLEMENTED_BUT_NOT_ACCEPTED | Packet builder includes identity, checkpoints, image hashes/paths, contact-sheet reference, expectations, bounded correlations, environment notes, and output contract, but not complete native/UIA/pixel/timeline summaries or a proven integrated packet. |
| 11.3 | COMPLETED_AND_PROVEN | Image/fact/checkpoint/note caps and total packet/instruction byte budget are enforced. |
| 11.4 | COMPLETED_AND_PROVEN | Generated instructions and packet reminders explicitly prohibit inferring identity, lease, cleanup, or root cause from images. |
| 11.5 | COMPLETED_AND_PROVEN | Portable path validation and packet tests reject/avoid absolute artifact-root paths; no credential source or model SDK exists. |
| 11.6 | COMPLETED_AND_PROVEN | Final packet bytes are written through the immutable store and their hash is exposed in scenario/manifest links for review binding. |
| 11.7 | IMPLEMENTED_BUT_NOT_ACCEPTED | Packet generation and image-tamper tests exist, but deterministic byte-for-byte generation and packet/result tamper coverage is incomplete. |

### 12. AI review-result contract

| ID | Classification | Evidence |
| --- | --- | --- |
| 12.1 | COMPLETED_AND_PROVEN | `VisualReviewResult` uses `tabdock-visual-review-result-v1`. |
| 12.2 | COMPLETED_AND_PROVEN | The four required verdict enum values are defined and deserialized as stable strings. |
| 12.3 | COMPLETED_AND_PROVEN | All requested finding categories are represented in `VisualFindingCategory`. |
| 12.4 | COMPLETED_AND_PROVEN | Findings require artifact/checkpoint/image hash fields and verifier cross-checks them against packet images. |
| 12.5 | COMPLETED_AND_PROVEN | Result findings contain concise description/expected/observed/uncertainty fields; no chain-of-thought field is required. |
| 12.6 | COMPLETED_AND_PROVEN | Findings accept and validate optional normalized rectangles in [0,1]. |
| 12.7 | COMPLETED_AND_PROVEN | Reviewer kind/ID are informational result fields and are never used as hash authority. |
| 12.8 | IMPLEMENTED_BUT_NOT_ACCEPTED | Verifier checks identity, IDs, reviewed checkpoints, verdict/finding consistency, paths, and file hashes, but the in-memory API accepts a caller-supplied packet hash and nullable required collections remain warning-prone. |

### 13. Canonical multimodal-agent workflow

| ID | Classification | Evidence |
| --- | --- | --- |
| 13.1 | COMPLETED_AND_PROVEN | `.agent/workflows/visual-evidence-review.md` exists as a harness-neutral workflow. |
| 13.2 | COMPLETED_AND_PROVEN | Workflow requires packet/hash validation, contact-sheet-first review, and full-resolution raw inspection. |
| 13.3 | COMPLETED_AND_PROVEN | Workflow explicitly requires describing visible symptoms before hypothesizing causes. |
| 13.4 | COMPLETED_AND_PROVEN | Workflow requires correlation with HWND/UIA/pixel/timeline/native evidence before source changes. |
| 13.5 | COMPLETED_AND_PROVEN | Workflow defines writing and offline-verifying `visual-review-result.json`. |
| 13.6 | COMPLETED_AND_PROVEN | Workflow's non-vision fallback requires honest `REVIEW_UNAVAILABLE` and forbids filename/metric inference. |
| 13.7 | COMPLETED_AND_PROVEN | No provider-specific adapter exists in the repository; the single canonical workflow is the applicable adapter surface, so no generated copy requires wiring. |
| 13.8 | IMPLEMENTED_BUT_NOT_ACCEPTED | Workflow discusses explicit packet selection, but the CLI switch is boolean and does not accept a packet path; multiple-run selection is not operationally wired. |

### 14. Review/qualification semantics

| ID | Classification | Evidence |
| --- | --- | --- |
| 14.1 | COMPLETED_AND_PROVEN | Visual verdict enums and native `ScenarioOutcomeKind` remain separate types/serialization vocabularies. |
| 14.2 | IMPLEMENTED_BUT_NOT_ACCEPTED | Native outcome is not mutated by visual code and workflow reminders state precedence, but current aggregation tests are tautological and no integrated visual gate proves every native failure precedence case. |
| 14.3 | IMPLEMENTED_BUT_NOT_ACCEPTED | A visual verdict model exists, but no catalog/gate path consumes `VISUAL_DEFECT` to block a required visual acceptance. |
| 14.4 | IMPLEMENTED_BUT_NOT_ACCEPTED | Workflow requires correlation, but code does not enforce correlation before a product/root-cause disposition. |
| 14.5 | IMPLEMENTED_BUT_NOT_ACCEPTED | Workflow describes required versus optional review, but no catalog scenario declares a required visual review and no gate records the distinction. |
| 14.6 | IMPLEMENTED_BUT_NOT_ACCEPTED | Attempt-numbered raw files preserve artifacts, but no visual-result aggregation prevents a later visual PASS from masking an earlier visual defect. |
| 14.7 | IMPLEMENTED_BUT_NOT_ACCEPTED | Native/visual separation is documented, but no disagreement state machine or aggregation implementation exists. |
| 14.8 | NOT_IMPLEMENTED | Only three non-aggregating outcome tests exist; exhaustive deterministic aggregation coverage is absent. |

### 15. Seeded visual-review fixtures

| ID | Classification | Evidence |
| --- | --- | --- |
| 15.1 | NOT_IMPLEMENTED | No healthy packet/image fixture with non-diagnostic naming exists. |
| 15.2 | NOT_IMPLEMENTED | No seeded occlusion fixture exists. |
| 15.3 | NOT_IMPLEMENTED | No seeded title/misalignment fixture exists. |
| 15.4 | NOT_IMPLEMENTED | No seeded wrong-guest/split-color packet fixture exists. |
| 15.5 | NOT_IMPLEMENTED | No seeded clipped/misplaced-popup fixture exists. |
| 15.6 | IMPLEMENTED_BUT_NOT_ACCEPTED | Programmatic packet/verifier tests exist, but no stable known review-result fixture set covers the seeded visual cases. |
| 15.7 | BLOCKED_SUPERVISED | No supervised exact-candidate packet review by a capable multimodal agent is present in current evidence. |
| 15.8 | NOT_IMPLEMENTED | No seeded reviewer prompt/fixture protocol exists that can prove the defective frame is undisclosed. |

### 16. Controlled image metrics/baselines

| ID | Classification | Evidence |
| --- | --- | --- |
| 16.1 | COMPLETED_AND_PROVEN | Existing brightness/frame-diff/dominant-channel semantics remain unchanged and pass `VisualPixelsRegressionTests`. |
| 16.2 | SUPERSEDED | The implementation deliberately adds no new region/perceptual comparator; visual images remain supplemental and controlled metric expansion is deferred. |
| 16.3 | SUPERSEDED | No perceptual metric was added, so there is no normalization/threshold implementation to qualify; the design explicitly rejects guessed universal thresholds. |
| 16.4 | SUPERSEDED | No universal exact-pixel golden comparator was introduced; the current design decision excludes that oracle rather than adding a meaningless negative-only feature. |
| 16.5 | IMPLEMENTED_BUT_NOT_ACCEPTED | Packet/workflow reminders and separate native/visual fields preserve the intended boundary, but no integrated gate proves metrics cannot substitute for retained/native evidence. |

### 17. Scenario integration — Wave 1 controlled fixtures

| ID | Classification | Evidence |
| --- | --- | --- |
| 17.1 | NOT_IMPLEMENTED | `tabswitch-hidesafety` contains no semantic visual checkpoint calls. |
| 17.2 | NOT_IMPLEMENTED | `minrestore` contains no semantic visual checkpoint calls. |
| 17.3 | NOT_IMPLEMENTED | `maximize-repro` contains no checkpoint/flight calls. |
| 17.4 | NOT_IMPLEMENTED | `guest-maximize-contained` contains no visual calls. |
| 17.5 | NOT_IMPLEMENTED | No split fixture scenario is visually integrated. |
| 17.6 | NOT_IMPLEMENTED | No context-menu/chrome fixture scenario is visually integrated. |
| 17.7 | NOT_IMPLEMENTED | No enabled-versus-disabled integrated scenario outcome comparison exists. |

### 18. Scenario integration — Wave 2 presentation integrity

| ID | Classification | Evidence |
| --- | --- | --- |
| 18.1 | NOT_IMPLEMENTED | Rename has no visual checkpoints. |
| 18.2 | NOT_IMPLEMENTED | Workspace/group menu interactions have no visual checkpoints. |
| 18.3 | NOT_IMPLEMENTED | Split enter/focus/end/resume has no visual checkpoints. |
| 18.4 | NOT_IMPLEMENTED | Inline `+` capture has no visual checkpoints. |
| 18.5 | NOT_IMPLEMENTED | Context-menu/chrome loops have no visual checkpoints. |
| 18.6 | NOT_IMPLEMENTED | Title-centering qualification has no visual evidence integration. |
| 18.7 | NOT_IMPLEMENTED | Controlled topmost guest qualification has no visual evidence integration. |
| 18.8 | NOT_IMPLEMENTED | No scenario selects flight mode only around a plausible transient transition. |

### 19. Scenario integration — Wave 3 real apps/topology

| ID | Classification | Evidence |
| --- | --- | --- |
| 19.1 | NOT_IMPLEMENTED | Browser F11 scenarios do not emit restricted visual packets. |
| 19.2 | NOT_IMPLEMENTED | No dual-monitor visual context capture is integrated. |
| 19.3 | NOT_IMPLEMENTED | No mixed-DPI before/after visual capture is integrated. |
| 19.4 | NOT_IMPLEMENTED | No adopted real-app visual cropping/minimization path is integrated. |
| 19.5 | COMPLETED_AND_PROVEN | Visual collection is opt-in via explicit CLI policy and defaults to `none`; real-app capture cannot begin from ordinary CI defaults. |
| 19.6 | NOT_IMPLEMENTED | The archived presentation-certification campaign has no visual infrastructure integration; its physical cells remain native-evidence-bound. |

### 20. Privacy and security hardening

| ID | Classification | Evidence |
| --- | --- | --- |
| 20.1 | COMPLETED_AND_PROVEN | Four requested privacy classes are defined and carried by scopes/artifacts. |
| 20.2 | COMPLETED_AND_PROVEN | Default policy is disabled and explicit routine scopes require target identity/privacy classification; test/product-owned classes are available. |
| 20.3 | COMPLETED_AND_PROVEN | Virtual desktop construction and capture require explicit authorization; safe defaults set it false and tests prove refusal. |
| 20.4 | COMPLETED_AND_PROVEN | Support-bundle code has no visual artifact inclusion path; visual artifacts are separate run-owned records, not implicit support-bundle entries. |
| 20.5 | NOT_IMPLEMENTED | `.visual-validation-runs/` is currently untracked and not ignored; no complete generated-visual exclusion rule exists. |
| 20.6 | COMPLETED_AND_PROVEN | No model SDK, HTTP upload, or external inference path exists in the implementation; packet generation is local/provider-neutral. |
| 20.7 | COMPLETED_AND_PROVEN | Proposal/design/workflow state that any future remote adapter requires explicit privacy authorization and secret handling. |
| 20.8 | IMPLEMENTED_BUT_NOT_ACCEPTED | Path, image-tamper, and some privacy checks exist, but the full path/privacy/packet/derived-failure abuse matrix is incomplete. |

### 21. Performance/resource qualification

| ID | Classification | Evidence |
| --- | --- | --- |
| 21.1 | NOT_IMPLEMENTED | Capture characterization exists, but no combined capture/encode/contact-sheet/packet measurement report exists. |
| 21.2 | IMPLEMENTED_BUT_NOT_ACCEPTED | Ring bytes and final retained bytes are observable counters, but peak memory and repeated lifecycle measurements are not reported. |
| 21.3 | COMPLETED_AND_PROVEN | `VisualEvidenceCounters` exposes requested/succeeded/failed/skipped, evicted/flushed, retained bytes, and encode milliseconds; manifests serialize snapshots. |
| 21.4 | NOT_IMPLEMENTED | Safe defaults are guesses; no measured controlled-run distribution has produced budgets. |
| 21.5 | IMPLEMENTED_BUT_NOT_ACCEPTED | Recorder operations are synchronous and stop in cleanup paths, but no resource-qualification evidence proves no surviving visual worker/timer. |
| 21.6 | IMPLEMENTED_BUT_NOT_ACCEPTED | Flush/Dispose/finally stop code exists, but cancellation/timeout activity tests are absent. |
| 21.7 | NOT_IMPLEMENTED | No visual-disabled resource qualification run compares the current infrastructure against the existing resource profiles. |

### 22. CI and deterministic gates

| ID | Classification | Evidence |
| --- | --- | --- |
| 22.1 | COMPLETED_AND_PROVEN | Ordinary policy defaults to `NONE`, no model dependency exists, and the current CI-safe validation path does not request screen capture. |
| 22.2 | COMPLETED_AND_PROVEN | Pure visual PNG/packet/verifier tests are linked into the unit project and run without HWNDs or model inference; 44/44 visual tests pass. |
| 22.3 | IMPLEMENTED_BUT_NOT_ACCEPTED | Negative image/path/stale tests exist, but the required full tamper/missing/derived/packet matrix is incomplete. |
| 22.4 | IMPLEMENTED_BUT_NOT_ACCEPTED | Historical no-visual behavior remains optional in source/verifier, but no dedicated compatibility test is present. |
| 22.5 | COMPLETED_AND_PROVEN | Current Debug solution build passed with 0 warnings/0 errors; Release build passed with 16 visual nullable warnings and 0 errors, so the task is checked only as a build execution result, not as a warning-free acceptance claim. |
| 22.6 | COMPLETED_AND_PROVEN | Current visual filter passed 44/44; the existing State records the broader 776/776 run, while a full current Debug/Release test gate remains part of successor closure. |
| 22.7 | COMPLETED_AND_PROVEN | Current solution build includes the ValidationDriver project path in the solution; prior checkpoint records the Release driver/GuineaPig build. |
| 22.8 | COMPLETED_AND_PROVEN | Prior checkpoint records 153/153 ValidationDriver self-tests and catalog/plan validation; source remains unchanged in this reconciliation. |
| 22.9 | COMPLETED_AND_PROVEN | Prior checkpoint records CI-safe Release validation/publish PASS; the current visual filter/build evidence does not invalidate that historical command result. |
| 22.10 | COMPLETED_AND_PROVEN | Repository-local OpenSpec status/instructions and the prior checkpoint both report strict planning validation completed; current artifacts are structurally valid. |

### 23. Supervised acceptance campaign

| ID | Classification | Evidence |
| --- | --- | --- |
| 23.1 | BLOCKED_SUPERVISED | Inspected run roots are historical native runs with candidate `fdb782e...`; no exact current visual candidate/lease packet is present. |
| 23.2 | BLOCKED_SUPERVISED | No current checkpoint-mode visual manifest or healthy packet exists in `.visual-validation-runs`. |
| 23.3 | BLOCKED_SUPERVISED | No seeded defective visual packet exists. |
| 23.4 | BLOCKED_SUPERVISED | No flight-recorder run or retained pre-failure frame sequence exists. |
| 23.5 | BLOCKED_CAPABILITY | No repository evidence records a capable multimodal agent opening current retained images and producing a result. |
| 23.6 | BLOCKED_CAPABILITY | No healthy/defect packet review pair exists to establish non-vacuous detection and no false positive. |
| 23.7 | IMPLEMENTED_BUT_NOT_ACCEPTED | Workflow specifies honest `REVIEW_UNAVAILABLE`, but no recorded result from an actual non-vision path is present. |
| 23.8 | COMPLETED_AND_PROVEN | Synthetic verifier tests rehash packet/image files and pass valid hash-bound reviews; physical acceptance remains separately blocked. |
| 23.9 | COMPLETED_AND_PROVEN | `TamperedImage_IsRejectedEvenWhenReviewHashIsUnchanged` proves an image-byte mutation invalidates verification. |
| 23.10 | IMPLEMENTED_BUT_NOT_ACCEPTED | Native outcome is kept separate in code and reminders, but no integrated visual review acceptance run proves failed lease/native precedence. |

### 24. Documentation and handoff

| ID | Classification | Evidence |
| --- | --- | --- |
| 24.1 | NOT_IMPLEMENTED | `docs/TESTING.md` documents native/resource qualification but not visual modes, artifact layout, ring behavior, or the review workflow. |
| 24.2 | NOT_IMPLEMENTED | `docs/release/qualification-control-plane.md` has no visual artifact/index/offline-verification documentation. |
| 24.3 | COMPLETED_AND_PROVEN | The canonical `.agent/workflows/visual-evidence-review.md` exists; no provider adapter copy requires synchronization. |
| 24.4 | COMPLETED_AND_PROVEN | `.agent/STATE.md` records visual implementation scope, schemas, validation evidence, blockers, and links the implementation/reconciliation investigations; this reconciliation removes the false 84/89 claim. |
| 24.5 | COMPLETED_AND_PROVEN | `docs/TESTING.md` and archived presentation-certification records explicitly keep visual evidence supplemental and physical outcomes native-evidence-bound. |
| 24.6 | BLOCKED_SUPERVISED | Archival is prohibited until the remaining implementation/acceptance obligations and exact provenance boundary are satisfied. |
| 24.7 | BLOCKED_SUPERVISED | The required clean commit/push cannot occur while the intended visual implementation and generated evidence remain dirty and supervised acceptance is absent; no staging or commit was performed. |

## Pre-disposition snapshot

The following counts and IDs are the historical state recorded before the
Milestone A disposition pass; they are retained for auditability and are not
the final status of the predecessor ledger.

- Canonical predecessor task total: **178**.
- `COMPLETED_AND_PROVEN` / checked before disposition: **93**.
- Unchecked before disposition: **85**.
  - `IMPLEMENTED_BUT_NOT_ACCEPTED`: 35.
  - `NOT_IMPLEMENTED`: 38.
  - `SUPERSEDED`: 3.
  - `BLOCKED_SUPERVISED`: 7.
  - `BLOCKED_CAPABILITY`: 2.

The exact unchecked task IDs before disposition were:

- Implemented but not accepted: `4.2`, `4.3`, `4.8`, `6.4`, `7.3`, `7.5`, `8.7`, `8.8`, `9.2`, `9.3`, `9.4`, `9.5`, `9.6`, `10.5`, `10.6`, `11.2`, `11.7`, `12.8`, `13.8`, `14.2`, `14.3`, `14.4`, `14.5`, `14.6`, `14.7`, `15.6`, `16.5`, `20.8`, `21.2`, `21.5`, `21.6`, `22.3`, `22.4`, `23.7`, `23.10`.
- Not implemented: `4.4`, `7.1`, `7.2`, `8.3`, `8.9`, `14.8`, `15.1`–`15.5`, `15.8`, `17.1`–`17.7`, `18.1`–`18.8`, `19.1`–`19.4`, `19.6`, `20.5`, `21.1`, `21.4`, `21.7`, `24.1`, `24.2`.
- Superseded: `16.2`, `16.3`, `16.4`.
- Blocked supervised: `15.7`, `23.1`–`23.4`, `24.6`, `24.7`.
- Blocked capability: `23.5`, `23.6`.

## Why 84/89 was not the canonical count

The old `84/89` statement was an implementation-only roll-up recorded in
`.agent/STATE.md` and the next-campaign planning material. It was not derived
from, nor joined to, the canonical 178 checkbox IDs. Git history shows the
canonical 178-task file was added as an all-unchecked planning ledger in
`108b389`, while the separate 84/89 wording was introduced later in the
planning checkpoint when the dirty visual implementation was summarized. No
mapping artifact exists that identifies which 84 of 89 rows correspond to the
178 canonical rows. The denominator therefore excluded or collapsed the
orientation, integration, privacy, acceptance, documentation, archive, and
push obligations. It must not be translated into a canonical checkbox count;
this reconciliation originally checked only the 93 rows with direct current
evidence and left the other 85 rows visibly unchecked; the final disposition
table below records the explicit treatment of every one of those 85 rows.

## Final row-level dispositions

The following table is the disposition ledger for all 85 rows that were
unchecked in the snapshot above. A disposition closes the predecessor row as
an auditable planning obligation; it does not turn a migrated or superseded
obligation into current product evidence.

Evidence keys:

- `A-run`: exact Milestone A Release qualification from candidate
  `b19e33e926d751c5be26a4684265a5cd368cef34`, executable SHA-256
  `b6418a949c08ac3d50e6460aaeb6ce3d01df545b95553efc4d98ca1a9cb031c7`,
  native ABI PASS, and a valid supervised desktop lease.
- `A-healthy`: exact run
  `96000be6-32c5-4fd4-860f-fc31bca5cae6`, packet SHA
  `daf8a966725e0fdd429d13e495fcb9d26c0139b8d9f2ec1430fb0901dea75633`,
  with five reviewed guest-window images.
- `A-defect`: exact run
  `c41e4302-b336-40f3-9006-9b500da8d010`; attempt 1 packet SHA
  `f76ea3a8382bb23d792ac0065e48c094cd23a3a54d45502c0540417ca8a4d964`
  reviewed as `VISUAL_DEFECT`, and rerun packet SHA
  `688f052e1523fde529e632dd170c4e2fc5ac5e9d53ddeb32654be63785741958`
  reviewed as `VISUAL_OK`.
- `A-flight`: exact run
  `3071f78b-7988-4c35-95e0-b4c96dd23a52`, packet SHA
  `2b54a390acd9d2b621f2a9f4ef7c2c2de42d89e53100d15f9264fc13d871681c`,
  with the explicit failure checkpoint, two pre-trigger frames, three flushed
  failure artifacts, and an empty stopped ring.
- `A-unavailable`: exact run
  `d07e2795-9f05-4cbd-91ca-b8dd9d628e1b`, packet SHA
  `442ebd7e626500fd68bbf85bd35cc549a2b37d78eb8a51803fcc1ab4ba6f43f2`,
  with an explicit `REVIEW_UNAVAILABLE` result, empty image/finding
  collections, and a non-empty capability note.
- `A-review`: the healthy, seeded-defect, and flight contact sheets plus all
  required raw PNGs were opened at full resolution; the reviewer wrote exact
  hash-bound results and did not infer identity, lease, cleanup, or cause from
  pixels.
- `A-tamper`: a byte-mutated exact-candidate PNG was rejected by offline
  verification with visual evidence hash mismatches.
- `A-gate`: 35/35 focused visual unit tests pass, including packet/verifier,
  tamper, review-unavailable, first-defect, and native/lease precedence cases;
  the reflection gate kept native failures unchanged and mapped required
  visual defect/unavailable outcomes to non-pass.
- `A-packet`: exact scenario/run/visual-manifest/result hierarchy and packet
  identity/hash bindings were verified for the supervised packets.
- `A-ci`: ordinary CI policy remains visual-disabled, and the exact candidate
  qualification run passed strict OpenSpec validation 38/38.
- `DOC`: `docs/TESTING.md` and
  `docs/release/qualification-control-plane.md` now document the visual
  modes, packet/index layout, privacy boundary, flight behavior, review
  contract, and offline hash verification.
- `SUCCESSOR`: the active
  `visual-evidence-closure-and-performance-requalification` change owns the
  explicitly remaining integration, measured-budget, cleanup, and final
  closure work; this is a destination, not evidence that those tasks are
  complete.
- `DPI`: the future DPI/topology campaign owns mixed-DPI, negative-coordinate,
  above-origin, and multi-monitor acceptance that Milestone A did not run.
- `REAL`: the future real-app campaign owns restricted browser/adopted-app
  imagery and privacy qualification.
- `SUPERSEDED`: the design intentionally excludes universal perceptual/golden
  image metrics; retained images and native facts remain authoritative.

| Row | Prior classification | Final disposition | Evidence / owner |
| --- | --- | --- | --- |
| 4.2 | IMPLEMENTED_BUT_NOT_ACCEPTED | MIGRATED_TO_SUCCESSOR | `SUCCESSOR`: container end-to-end capture remains outside A. |
| 4.3 | IMPLEMENTED_BUT_NOT_ACCEPTED | COMPLETED_AND_PROVEN | `A-healthy`, `A-defect`: exact guest packets carry strong identity metadata. |
| 4.4 | NOT_IMPLEMENTED | MIGRATED_TO_SUCCESSOR | `SUCCESSOR`: dedicated owned-popup admission remains open. |
| 4.8 | IMPLEMENTED_BUT_NOT_ACCEPTED | MIGRATED_TO_DPI_TOPOLOGY_CAMPAIGN | `DPI`: full geometry/DPI matrix remains unrun. |
| 6.4 | IMPLEMENTED_BUT_NOT_ACCEPTED | COMPLETED_AND_PROVEN | `A-ci`: ordinary CI uses disabled visual policy; strict qualification passed. |
| 7.1 | NOT_IMPLEMENTED | MIGRATED_TO_SUCCESSOR | `SUCCESSOR`: automatic assertion-hook capture remains open. |
| 7.2 | NOT_IMPLEMENTED | MIGRATED_TO_SUCCESSOR | `SUCCESSOR`: recursive assertion-capture guard remains open. |
| 7.3 | IMPLEMENTED_BUT_NOT_ACCEPTED | COMPLETED_AND_PROVEN | `A-flight`: failure checkpoint and trigger timeline are retained. |
| 7.5 | IMPLEMENTED_BUT_NOT_ACCEPTED | MIGRATED_TO_SUCCESSOR | `SUCCESSOR`: thrown/timeout automatic-capture cleanup remains open. |
| 8.3 | NOT_IMPLEMENTED | COMPLETED_AND_PROVEN | `A-flight`: recording is marked only around `maximize-repro` high-risk transition. |
| 8.7 | IMPLEMENTED_BUT_NOT_ACCEPTED | MIGRATED_TO_SUCCESSOR | `SUCCESSOR`: cancellation/timeout/process-exit/abort proof remains open. |
| 8.8 | IMPLEMENTED_BUT_NOT_ACCEPTED | MIGRATED_TO_SUCCESSOR | `SUCCESSOR`: full cancellation and memory-limit matrix remains open. |
| 8.9 | NOT_IMPLEMENTED | MIGRATED_TO_SUCCESSOR | `SUCCESSOR`: measured overhead budget remains an active qualification task. |
| 9.2 | IMPLEMENTED_BUT_NOT_ACCEPTED | COMPLETED_AND_PROVEN | `A-packet`: exact visual links are present in the scenario/run hierarchy. |
| 9.3 | IMPLEMENTED_BUT_NOT_ACCEPTED | MIGRATED_TO_SUCCESSOR | `SUCCESSOR`: final bundle-index qualification remains in successor closure. |
| 9.4 | IMPLEMENTED_BUT_NOT_ACCEPTED | COMPLETED_AND_PROVEN | `A-packet`, `A-tamper`, `A-gate`: offline binding and mutation failures are exercised. |
| 9.5 | IMPLEMENTED_BUT_NOT_ACCEPTED | COMPLETED_AND_PROVEN | `A-ci`: historical no-visual compatibility remains a separate accepted path. |
| 9.6 | IMPLEMENTED_BUT_NOT_ACCEPTED | COMPLETED_AND_PROVEN | `A-tamper`, `A-gate`: image, metadata, path, candidate, and scenario binding checks are covered. |
| 10.5 | IMPLEMENTED_BUT_NOT_ACCEPTED | COMPLETED_AND_PROVEN | `A-gate`: deterministic contact-sheet/packet boundary tests pass. |
| 10.6 | IMPLEMENTED_BUT_NOT_ACCEPTED | COMPLETED_AND_PROVEN | `A-gate`: raw evidence survives derived contact-sheet failure with explicit failure data. |
| 11.2 | IMPLEMENTED_BUT_NOT_ACCEPTED | COMPLETED_AND_PROVEN | `A-packet`: exact packets include identity, checkpoints, images, expectations, facts, timeline, notes, and contract. |
| 11.7 | IMPLEMENTED_BUT_NOT_ACCEPTED | COMPLETED_AND_PROVEN | `A-packet`, `A-tamper`: deterministic packet and tamper verification passes. |
| 12.8 | IMPLEMENTED_BUT_NOT_ACCEPTED | COMPLETED_AND_PROVEN | `A-gate`, `A-tamper`: stale identity, required review, artifact, verdict, and byte checks reject invalid evidence. |
| 13.8 | IMPLEMENTED_BUT_NOT_ACCEPTED | MIGRATED_TO_SUCCESSOR | `SUCCESSOR`: operational multi-run packet selection remains open. |
| 14.2 | IMPLEMENTED_BUT_NOT_ACCEPTED | COMPLETED_AND_PROVEN | `A-gate`: visual OK never promotes native product/harness/environment/lease failures. |
| 14.3 | IMPLEMENTED_BUT_NOT_ACCEPTED | COMPLETED_AND_PROVEN | `A-gate`, `A-defect`: required visual defect maps to non-pass. |
| 14.4 | IMPLEMENTED_BUT_NOT_ACCEPTED | MIGRATED_TO_SUCCESSOR | `SUCCESSOR`: correlation-before-root-cause remains a workflow/process obligation. |
| 14.5 | IMPLEMENTED_BUT_NOT_ACCEPTED | COMPLETED_AND_PROVEN | `A-gate`, `A-unavailable`: required and optional unavailable semantics are distinct. |
| 14.6 | IMPLEMENTED_BUT_NOT_ACCEPTED | COMPLETED_AND_PROVEN | `A-defect`, `A-gate`: first visual defect is retained across rerun and cannot be best-of-N promoted. |
| 14.7 | IMPLEMENTED_BUT_NOT_ACCEPTED | MIGRATED_TO_SUCCESSOR | `SUCCESSOR`: explicit AI/native/human disagreement state remains open. |
| 14.8 | NOT_IMPLEMENTED | COMPLETED_AND_PROVEN | `A-gate`: deterministic aggregation combinations pass. |
| 15.1 | NOT_IMPLEMENTED | COMPLETED_AND_PROVEN | `A-healthy`: test-owned healthy images use neutral artifact names. |
| 15.2 | NOT_IMPLEMENTED | MIGRATED_TO_SUCCESSOR | `SUCCESSOR`: seeded occlusion fixture remains open. |
| 15.3 | NOT_IMPLEMENTED | MIGRATED_TO_SUCCESSOR | `SUCCESSOR`: seeded title/misalignment fixture remains open. |
| 15.4 | NOT_IMPLEMENTED | COMPLETED_AND_PROVEN | `A-defect`: controlled wrong-guest/split-color seed is detected. |
| 15.5 | NOT_IMPLEMENTED | MIGRATED_TO_SUCCESSOR | `SUCCESSOR`: seeded clipped/misplaced-popup fixture remains open. |
| 15.6 | IMPLEMENTED_BUT_NOT_ACCEPTED | COMPLETED_AND_PROVEN | `A-gate`: known-result verifier tests run without model inference. |
| 15.7 | BLOCKED_SUPERVISED | COMPLETED_AND_PROVEN | `A-review`: capable multimodal review produced valid healthy/defect results. |
| 15.8 | NOT_IMPLEMENTED | COMPLETED_AND_PROVEN | `A-review`: review instructions state expectations but do not identify a defective frame. |
| 16.2 | SUPERSEDED | ACCEPTED_SUPERSEDED | `SUPERSEDED`: no tolerant region metric is part of the accepted design. |
| 16.3 | SUPERSEDED | ACCEPTED_SUPERSEDED | `SUPERSEDED`: no perceptual metric or guessed threshold was added. |
| 16.4 | SUPERSEDED | ACCEPTED_SUPERSEDED | `SUPERSEDED`: universal exact-pixel golden comparison remains excluded. |
| 16.5 | IMPLEMENTED_BUT_NOT_ACCEPTED | COMPLETED_AND_PROVEN | `A-gate`, `A-packet`: image metrics remain supplemental to retained/native evidence. |
| 17.1 | NOT_IMPLEMENTED | MIGRATED_TO_SUCCESSOR | `SUCCESSOR`: `tabswitch-hidesafety` integration remains open. |
| 17.2 | NOT_IMPLEMENTED | MIGRATED_TO_SUCCESSOR | `SUCCESSOR`: `minrestore` integration remains open. |
| 17.3 | NOT_IMPLEMENTED | COMPLETED_AND_PROVEN | `A-flight`: `maximize-repro` uses checkpoints and bounded flight evidence. |
| 17.4 | NOT_IMPLEMENTED | MIGRATED_TO_SUCCESSOR | `SUCCESSOR`: `guest-maximize-contained` integration remains open. |
| 17.5 | NOT_IMPLEMENTED | MIGRATED_TO_SUCCESSOR | `SUCCESSOR`: split fixture integration remains open. |
| 17.6 | NOT_IMPLEMENTED | MIGRATED_TO_SUCCESSOR | `SUCCESSOR`: context-menu/chrome fixture integration remains open. |
| 17.7 | NOT_IMPLEMENTED | COMPLETED_AND_PROVEN | `A-gate`: optional visual outcomes preserve native qualification. |
| 18.1 | NOT_IMPLEMENTED | MIGRATED_TO_SUCCESSOR | `SUCCESSOR`: rename checkpoints remain open. |
| 18.2 | NOT_IMPLEMENTED | MIGRATED_TO_SUCCESSOR | `SUCCESSOR`: workspace/group menu checkpoints remain open. |
| 18.3 | NOT_IMPLEMENTED | MIGRATED_TO_SUCCESSOR | `SUCCESSOR`: split lifecycle checkpoints remain open. |
| 18.4 | NOT_IMPLEMENTED | MIGRATED_TO_SUCCESSOR | `SUCCESSOR`: inline capture checkpoints remain open. |
| 18.5 | NOT_IMPLEMENTED | MIGRATED_TO_SUCCESSOR | `SUCCESSOR`: context-menu/chrome rendering checkpoints remain open. |
| 18.6 | NOT_IMPLEMENTED | MIGRATED_TO_DPI_TOPOLOGY_CAMPAIGN | `DPI`: title-centering evidence requires physical topology coverage. |
| 18.7 | NOT_IMPLEMENTED | MIGRATED_TO_SUCCESSOR | `SUCCESSOR`: controlled topmost guest integration remains open. |
| 18.8 | NOT_IMPLEMENTED | COMPLETED_AND_PROVEN | `A-flight`: flight mode is limited to the plausible transient maximize transition. |
| 19.1 | NOT_IMPLEMENTED | MIGRATED_TO_REAL_APP_CAMPAIGN | `REAL`: restricted browser F11 evidence requires a separate privacy campaign. |
| 19.2 | NOT_IMPLEMENTED | MIGRATED_TO_DPI_TOPOLOGY_CAMPAIGN | `DPI`: dual-monitor transfer remains unrun. |
| 19.3 | NOT_IMPLEMENTED | MIGRATED_TO_DPI_TOPOLOGY_CAMPAIGN | `DPI`: mixed-DPI before/after capture remains unrun. |
| 19.4 | NOT_IMPLEMENTED | MIGRATED_TO_REAL_APP_CAMPAIGN | `REAL`: adopted real-app crop/minimization remains unrun. |
| 19.6 | NOT_IMPLEMENTED | MIGRATED_TO_SUCCESSOR | `SUCCESSOR`: integration with physical certification remains a separate factual handoff. |
| 20.5 | NOT_IMPLEMENTED | COMPLETED_AND_PROVEN | `DOC`, `A-ci`: generated visual roots are ignored and check-ignore confirms the rules. |
| 20.8 | IMPLEMENTED_BUT_NOT_ACCEPTED | COMPLETED_AND_PROVEN | `A-tamper`, `A-gate`: path/privacy/hash abuse checks pass. |
| 21.1 | NOT_IMPLEMENTED | MIGRATED_TO_SUCCESSOR | `SUCCESSOR`: combined measured latency report remains open. |
| 21.2 | IMPLEMENTED_BUT_NOT_ACCEPTED | MIGRATED_TO_SUCCESSOR | `SUCCESSOR`: repeated peak-memory/resource qualification remains open. |
| 21.4 | NOT_IMPLEMENTED | MIGRATED_TO_SUCCESSOR | `SUCCESSOR`: measured conservative budgets remain open. |
| 21.5 | IMPLEMENTED_BUT_NOT_ACCEPTED | MIGRATED_TO_SUCCESSOR | `SUCCESSOR`: no-worker-after-cleanup proof remains open. |
| 21.6 | IMPLEMENTED_BUT_NOT_ACCEPTED | MIGRATED_TO_SUCCESSOR | `SUCCESSOR`: cancellation/timeout lifecycle proof remains open. |
| 21.7 | NOT_IMPLEMENTED | MIGRATED_TO_SUCCESSOR | `SUCCESSOR`: disabled visual resource qualification remains open. |
| 22.3 | IMPLEMENTED_BUT_NOT_ACCEPTED | COMPLETED_AND_PROVEN | `A-tamper`, `A-gate`: deterministic negative tamper/stale/missing/path tests pass. |
| 22.4 | IMPLEMENTED_BUT_NOT_ACCEPTED | COMPLETED_AND_PROVEN | `A-ci`, `A-gate`: historical non-visual compatibility is retained and tested. |
| 23.1 | BLOCKED_SUPERVISED | COMPLETED_AND_PROVEN | `A-run`: exact b19 candidate ran on an exclusive cleared desktop with a valid lease. |
| 23.2 | BLOCKED_SUPERVISED | COMPLETED_AND_PROVEN | `A-healthy`: exact checkpoint packet was reviewed and verified. |
| 23.3 | BLOCKED_SUPERVISED | COMPLETED_AND_PROVEN | `A-defect`: exact seeded defect packet is retained and hash-bound. |
| 23.4 | BLOCKED_SUPERVISED | COMPLETED_AND_PROVEN | `A-flight`: exact transient failure flush retained ordered pre-trigger history. |
| 23.5 | BLOCKED_CAPABILITY | COMPLETED_AND_PROVEN | `A-review`: capable multimodal inspection produced valid hash-bound results. |
| 23.6 | BLOCKED_CAPABILITY | COMPLETED_AND_PROVEN | `A-review`, `A-defect`: seeded defect caught and healthy packet remained accepted. |
| 23.7 | IMPLEMENTED_BUT_NOT_ACCEPTED | COMPLETED_AND_PROVEN | `A-unavailable`: non-vision result is explicit, empty, and capability-noted. |
| 23.10 | IMPLEMENTED_BUT_NOT_ACCEPTED | COMPLETED_AND_PROVEN | `A-gate`: native/lease precedence reflection cases remain non-pass under visual OK. |
| 24.1 | NOT_IMPLEMENTED | COMPLETED_AND_PROVEN | `DOC`: testing guide now covers the complete visual packet/review contract. |
| 24.2 | NOT_IMPLEMENTED | COMPLETED_AND_PROVEN | `DOC`: release control-plane guide now covers visual indexing/offline verification. |
| 24.6 | BLOCKED_SUPERVISED | MIGRATED_TO_SUCCESSOR | `SUCCESSOR`: successor task 3.7/E6.6 owns sync/archive after this disposition boundary. |
| 24.7 | BLOCKED_SUPERVISED | MIGRATED_TO_SUCCESSOR | `SUCCESSOR`: final metadata, push, and origin-parity proof remain final closure work. |

Disposition totals: **85/85 rows explicitly dispositioned**, with
**41 `COMPLETED_AND_PROVEN`**, **3 `ACCEPTED_SUPERSEDED`**, **35
`MIGRATED_TO_SUCCESSOR`**, **4 `MIGRATED_TO_DPI_TOPOLOGY_CAMPAIGN`**, and
**2 `MIGRATED_TO_REAL_APP_CAMPAIGN`**. No predecessor row remains without one
of the seven allowed dispositions. The active successor remains open for its
own remaining tasks; this table is not a claim that those migrated tasks are
complete.
