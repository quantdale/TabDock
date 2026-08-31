# Tasks — visual evidence and AI-assisted presentation review

> **Preserved on main as future work (2026-09-01).** Repository
> consolidation does not implement or partially claim this checklist. The
> completed presentation-certification evidence remains hard-evidence-bound
> and is not retroactively dependent on visual packets or AI review.

## 0. Orientation and scope lock

- [ ] 0.1 Resolve current `HEAD`, `origin/main`, branch and worktree dynamically before implementation; do not rely on SHAs embedded in this plan.
- [ ] 0.2 Read `AGENTS.md`, `.agent/STATE.md`, `docs/TESTING.md`, the active presentation-integrity physical-certification change, the canonical qualification specs, and current ValidationDriver artifact code before editing.
- [ ] 0.3 Inventory all current pixel-capture callers, artifact writers, run manifests, bundle verifiers, timeline records, and cleanup paths.
- [ ] 0.4 Confirm this campaign changes validation/evidence infrastructure only. Do not change production TabDock behavior unless a separate valid defect investigation proves a product change is necessary.
- [ ] 0.5 Record a concise implementation plan/investigation checkpoint under `.agent/` and link it from `.agent/STATE.md`.

## 1. Current-capture baseline

- [ ] 1.1 Characterize `Pixels.CaptureHostScreenArea` and `CaptureWindowViaPrintWindow` on the current controlled GuineaPig fixtures.
- [ ] 1.2 Verify channel order, dimensions, screen/client coordinate translation, DWM-composited semantics, and failure behavior.
- [ ] 1.3 Measure capture latency and raw memory for representative small/default/large windows.
- [ ] 1.4 Confirm which existing scenarios already rely on brightness, frame diff, dominant color, screen pixels, or PrintWindow so the new layer preserves their semantics.
- [ ] 1.5 Add regression tests before refactoring any shared pixel code.

## 2. Visual evidence domain model

- [ ] 2.1 Add versioned types for `VisualFrame`, `VisualCaptureScope`, `VisualCheckpointPhase`, `VisualCheckpointRequest`, `VisualArtifactRecord`, and `VisualEvidencePolicy`.
- [ ] 2.2 Define stable enums/IDs for capture method, scope, privacy class, evidence level, checkpoint phase, and required-vs-optional capture.
- [ ] 2.3 Ensure all paths are normalized relative paths below the run-owned artifact root.
- [ ] 2.4 Ensure target identity metadata uses existing run-safe process/HWND identity conventions rather than inventing a weaker title-based identity.
- [ ] 2.5 Unit-test serialization, invalid enums, malformed rectangles, empty dimensions, and unsafe paths.

## 3. PNG encoding and immutable artifacts

- [ ] 3.1 Implement lossless PNG encoding for the current 32-bit pixel representation without introducing a large new imaging dependency unless proven necessary.
- [ ] 3.2 Add deterministic tests proving red/green/blue channel correctness and exact width/height preservation.
- [ ] 3.3 Write images through a bounded temporary path and atomic finalization under the attempt artifact root.
- [ ] 3.4 Compute SHA-256 over the final PNG bytes and store size/MIME/hash in the artifact record.
- [ ] 3.5 Reject absolute paths, traversal, duplicate artifact IDs, duplicate output paths, and writes outside the artifact root.
- [ ] 3.6 Make registered raw PNGs immutable from the recorder's perspective; derived annotations/contact sheets use separate files.
- [ ] 3.7 Add cleanup behavior for partial temp files after exceptions/cancellation.

## 4. Capture-scope implementation

- [ ] 4.1 Implement `HostClient` screen-composited capture using the existing BitBlt semantics.
- [ ] 4.2 Implement `ContainerWindow` screen-composited capture clipped to the virtual screen.
- [ ] 4.3 Implement approved `GuestWindow` capture with current strong identity validation.
- [ ] 4.4 Implement `OwnedPopup` capture for known TabDock-owned popup/dialog HWNDs.
- [ ] 4.5 Implement `TargetWithContext` using a small bounded margin clipped to the relevant monitor work area.
- [ ] 4.6 Keep `VirtualDesktop` disabled by default and behind an explicit supervised diagnostics policy if it is implemented at all.
- [ ] 4.7 Record requested and actual screen rectangles, monitor, DPI, capture method, target identity, and privacy classification.
- [ ] 4.8 Unit-test clipping at negative coordinates, above-origin monitors, narrow regions, off-screen rectangles, zero-size windows, stale HWNDs, and DPI variations using seams/fakes where possible.

## 5. Recorder and checkpoint API

- [ ] 5.1 Add one scenario-facing `ctx.Visual`-style API so scenarios request semantic checkpoints rather than files directly.
- [ ] 5.2 Support `Baseline`, `BeforeAction`, `AfterActionImmediate`, `AfterActionSettled`, `BeforeAssertion`, `Suspicious`, `AssertionFailure`, `FinalHealthy`, and `BeforeCleanup` phases.
- [ ] 5.3 Require every retained checkpoint to include a stable checkpoint ID and explicit visual expectation.
- [ ] 5.4 Support multiple approved scopes per checkpoint without duplicating scenario plumbing.
- [ ] 5.5 Distinguish required capture from best-effort capture.
- [ ] 5.6 If required capture fails, surface `FAIL_HARNESS`; never silently continue to a visual PASS.
- [ ] 5.7 If optional capture fails, retain explicit unavailable metadata without relabeling native results.
- [ ] 5.8 Add artifact count and byte ceilings; prove the recorder stops accepting additional optional evidence after the limit rather than growing unbounded.
- [ ] 5.9 Record capture/encode duration and retained byte counters.

## 6. CLI and policy controls

- [ ] 6.1 Add a bounded CLI/config policy for visual evidence levels equivalent to `none`, `failure`, `checkpoints`, and `flight`.
- [ ] 6.2 Add an explicit switch to build AI visual-review packets.
- [ ] 6.3 Add bounded maximum-byte/count controls with safe repository defaults.
- [ ] 6.4 Ensure headless/CI modes do not unexpectedly begin desktop capture.
- [ ] 6.5 Ensure physical runs do not expand from test-owned regions to unrestricted desktop imagery because a user selected a higher evidence level.
- [ ] 6.6 Include the effective visual policy in result/run manifests.

## 7. Automatic failure capture

- [ ] 7.1 Hook the scenario assertion/error path so a required/suspicious failure can request one final visual checkpoint before cleanup when the lease/evidence scope remains valid.
- [ ] 7.2 Avoid recursive failures when screenshot capture itself fails during an assertion failure.
- [ ] 7.3 Record which assertion/action triggered the failure capture.
- [ ] 7.4 Preserve first-attempt failure screenshots across reruns.
- [ ] 7.5 Verify cleanup still runs when failure capture throws or times out.

## 8. Bounded visual flight recorder

- [ ] 8.1 Implement a per-scenario `VisualRingBuffer` with a hard frame-count, frame-rate, duration, and memory ceiling.
- [ ] 8.2 Default to no more than roughly two samples per second and a short single-digit-second history; keep exact defaults centralized and testable.
- [ ] 8.3 Start recording only around explicitly marked high-risk transitions.
- [ ] 8.4 Keep rolling frames in memory and discard them on healthy completion unless a policy explicitly asks to retain them.
- [ ] 8.5 On `Suspicious`/`AssertionFailure`, flush the ordered pre-failure frames plus the failure frame to immutable PNG artifacts.
- [ ] 8.6 Label each flushed frame by relative time from the triggering checkpoint.
- [ ] 8.7 Stop recorder work in `finally`, cancellation, timeout, process exit, and scenario abort paths.
- [ ] 8.8 Add deterministic eviction/order/cancellation/memory-limit tests.
- [ ] 8.9 Measure recorder overhead and prove it remains within a documented bounded budget.

## 9. Visual manifest integration

- [ ] 9.1 Add `visual-manifest.json` per attempt/scenario containing all visual artifact records.
- [ ] 9.2 Link the visual manifest from the existing scenario result and run-manifest hierarchy without breaking old non-visual runs.
- [ ] 9.3 Extend qualification-bundle indexing to include declared visual artifacts by hash when present.
- [ ] 9.4 Update offline verification to reject missing, modified, duplicated, path-escaping, stale-run, stale-candidate, or schema-invalid visual evidence.
- [ ] 9.5 Preserve compatibility with historical bundles that contain no visual section.
- [ ] 9.6 Add tamper tests that mutate one PNG byte, metadata hash, relative path, candidate binding, and scenario binding.

## 10. Contact sheet and derived evidence

- [ ] 10.1 Implement deterministic chronological contact-sheet generation for selected checkpoints.
- [ ] 10.2 Include checkpoint ID, phase, short expectation and relative time outside the thumbnail image area.
- [ ] 10.3 Mark contact sheets as derived evidence and retain raw image hashes separately.
- [ ] 10.4 Never paint annotations into the authoritative raw PNG.
- [ ] 10.5 Add tests for zero/one/many images, mixed dimensions, large labels, and packet byte limits.
- [ ] 10.6 If contact-sheet generation fails, preserve valid raw visual evidence and report the derived artifact failure explicitly.

## 11. AI visual-review packet

- [ ] 11.1 Define and version `tabdock-visual-review-packet-v1`.
- [ ] 11.2 Build one packet per selected scenario attempt containing candidate/run/scenario identity, ordered checkpoints, raw image relative paths and hashes, contact-sheet reference, expectations, native/UIA/pixel summaries, relevant timeline offsets, environment variation notes, and required output contract.
- [ ] 11.3 Keep packets bounded; include correlated facts rather than entire logs.
- [ ] 11.4 Include explicit reminders that images cannot prove process identity, lease validity, cleanup correctness, or cause.
- [ ] 11.5 Ensure packets contain no absolute machine paths or credentials.
- [ ] 11.6 Hash the finalized packet and expose the packet hash for review-result binding.
- [ ] 11.7 Add deterministic packet generation and tamper tests.

## 12. AI review-result contract

- [ ] 12.1 Define and version `tabdock-visual-review-result-v1`.
- [ ] 12.2 Support verdicts `VISUAL_OK`, `VISUAL_SUSPECT`, `VISUAL_DEFECT`, and `REVIEW_UNAVAILABLE`.
- [ ] 12.3 Define finding categories for occlusion, blank/black region, wrong guest, clipping, misalignment, popup placement, z-order composition, transient flicker/flash, stale frame, visible geometry drift, DPI anomaly, unexpected chrome, capture artifact, and other.
- [ ] 12.4 Require every concrete finding to reference checkpoint/artifact IDs and the exact reviewed image hashes.
- [ ] 12.5 Permit a concise observable explanation and uncertainty list; do not require hidden chain-of-thought.
- [ ] 12.6 Add optional normalized region coordinates for findings.
- [ ] 12.7 Record reviewer kind/harness/model only as informational provenance, not as a trust substitute for hashes.
- [ ] 12.8 Build a verifier that rejects stale packet hashes, stale candidate/run IDs, nonexistent artifact IDs, unreviewed required images, invalid verdicts, malformed regions, and hash mismatches.

## 13. Canonical multimodal-agent workflow

- [ ] 13.1 Add one harness-neutral canonical workflow under `.agent/workflows/` for visual-evidence review.
- [ ] 13.2 Tell the agent to validate the packet first, open the contact sheet, then inspect suspicious raw frames at full resolution.
- [ ] 13.3 Tell the agent to describe visible symptoms before hypothesizing causes.
- [ ] 13.4 Tell the agent to correlate findings with HWND/UIA/pixel/timeline evidence before authorizing source changes.
- [ ] 13.5 Tell the agent to write and verify `visual-review-result.json`.
- [ ] 13.6 Tell non-vision agents to return `REVIEW_UNAVAILABLE` rather than infer from file names/metrics.
- [ ] 13.7 Wire any provider-specific agent adapters to the canonical workflow through the existing agent-config sync mechanism; do not hand-edit generated copies.
- [ ] 13.8 Document how a developer explicitly supplies a review packet path when multiple runs exist.

## 14. Review/qualification semantics

- [ ] 14.1 Keep `VISUAL_*` separate from the existing scenario outcome vocabulary.
- [ ] 14.2 Prove `VISUAL_OK` cannot override `FAIL_PRODUCT`, `FAIL_HARNESS`, blocked lease, identity failure, or native assertion failure.
- [ ] 14.3 Make `VISUAL_DEFECT` block visual acceptance for a scenario/gate that declares visual review required.
- [ ] 14.4 Require normal evidence correlation before converting a visual finding into a product defect/root-cause claim.
- [ ] 14.5 Treat `REVIEW_UNAVAILABLE` as non-pass only for gates that explicitly require a visual review; keep it informational otherwise.
- [ ] 14.6 Retain first valid visual defects across reruns and prevent best-of-N visual PASS promotion.
- [ ] 14.7 Define disagreement handling: AI/native/human disagreement remains unresolved/suspect, never averaged into PASS.
- [ ] 14.8 Add deterministic aggregation tests for all combinations.

## 15. Seeded visual-review fixtures

- [ ] 15.1 Create test-owned healthy visual packet fixtures that are not trivially labeled "healthy" in the image/file name.
- [ ] 15.2 Create a seeded occlusion fixture.
- [ ] 15.3 Create a seeded title/misalignment fixture.
- [ ] 15.4 Create a seeded wrong-guest/split-color fixture.
- [ ] 15.5 Create a seeded clipped/misplaced-popup fixture where practical.
- [ ] 15.6 Add deterministic verifier tests for known review-result fixtures without requiring an AI model in CI.
- [ ] 15.7 During supervised implementation validation, have a capable multimodal agent inspect at least one healthy and one defective packet and prove the workflow produces a valid hash-bound result.
- [ ] 15.8 Do not tell the reviewer which seeded frame is defective in the review prompt itself; preserve non-vacuous evaluation.

## 16. Controlled image metrics/baselines

- [ ] 16.1 Keep existing brightness/frame-diff/dominant-color tests working.
- [ ] 16.2 Evaluate region-based/tolerant comparisons only for controlled GuineaPig fixtures.
- [ ] 16.3 If a perceptual metric is added, document its normalization, DPI/resize policy, capture method, and fixture-specific threshold derivation.
- [ ] 16.4 Add negative tests showing a universal exact-pixel golden comparison is not used for real apps or mixed Windows rendering environments.
- [ ] 16.5 Treat deterministic image metrics as an additional signal, not a substitute for retained images or native evidence.

## 17. Scenario integration — Wave 1 controlled fixtures

- [ ] 17.1 Integrate checkpoints with `tabswitch-hidesafety`.
- [ ] 17.2 Integrate checkpoints with `minrestore`.
- [ ] 17.3 Integrate checkpoints/flight mode with `maximize-repro`.
- [ ] 17.4 Integrate checkpoints with `guest-maximize-contained`.
- [ ] 17.5 Integrate one split fixture scenario.
- [ ] 17.6 Integrate one context-menu/chrome fixture scenario.
- [ ] 17.7 Verify existing scenario outcomes do not change merely because optional visual evidence is enabled.

## 18. Scenario integration — Wave 2 presentation integrity

- [ ] 18.1 Add baseline/pre/post/settled visual checkpoints to rename.
- [ ] 18.2 Add checkpoints to workspace/group menu interactions.
- [ ] 18.3 Add checkpoints to split enter/focus/end/resume.
- [ ] 18.4 Add checkpoints to inline `+` capture open/close/cancel/capture.
- [ ] 18.5 Add checkpoints to context-menu and chrome-click rendering loops.
- [ ] 18.6 Add visual evidence to title-centering physical measurement.
- [ ] 18.7 Add visual evidence to the controlled topmost guest scenario.
- [ ] 18.8 Choose flight-recorder use only for transitions with a plausible transient defect; do not enable it everywhere.

## 19. Scenario integration — Wave 3 real apps/topology

- [ ] 19.1 After privacy gates are proven, add restricted visual packets to real browser F11 qualification.
- [ ] 19.2 Add bounded context captures for dual-monitor transfer.
- [ ] 19.3 Add mixed-DPI before/after captures with monitor/DPI metadata.
- [ ] 19.4 Minimize/crop adopted real-app imagery and avoid unrelated desktop content.
- [ ] 19.5 Require explicit real-app visual-evidence policy; do not make it default CI behavior.
- [ ] 19.6 Reuse the visual infrastructure in the active presentation-integrity physical-certification campaign without marking its physical cells PASS unless their original requirements are actually satisfied.

## 20. Privacy and security hardening

- [ ] 20.1 Add privacy classes `TestOwned`, `ProductOwned`, `RealAppRestricted`, and `DesktopRestricted` or equivalent.
- [ ] 20.2 Default routine capture/review eligibility to test-owned/product-owned scopes.
- [ ] 20.3 Prove whole-desktop capture is disabled by default.
- [ ] 20.4 Ensure support bundles do not start including screenshots implicitly.
- [ ] 20.5 Ensure generated screenshots/review packets/results are ignored and cannot be accidentally committed through normal validation.
- [ ] 20.6 Ensure external-model upload is not part of this implementation.
- [ ] 20.7 Document that any future remote model adapter requires explicit operator privacy authorization and secret handling.
- [ ] 20.8 Add path/privacy/tamper abuse tests.

## 21. Performance/resource qualification

- [ ] 21.1 Measure capture, encoding, contact-sheet and packet-build latency.
- [ ] 21.2 Measure peak ring-buffer memory and final retained bytes.
- [ ] 21.3 Add explicit counters for captures requested/succeeded/failed/skipped, frames evicted/flushed, bytes retained, and encode time.
- [ ] 21.4 Define conservative harness budgets from measured controlled runs rather than guesses.
- [ ] 21.5 Prove no visual worker/timer survives scenario cleanup.
- [ ] 21.6 Prove cancellation and timeout stop all recorder activity.
- [ ] 21.7 Run the existing resource qualification to ensure visual infrastructure does not regress unrelated headless/native lifecycle behavior when disabled.

## 22. CI and deterministic gates

- [ ] 22.1 Keep real screen capture and multimodal inference out of ordinary CI.
- [ ] 22.2 Add synthetic in-memory PNG fixtures and packet/verifier self-tests to CI-safe validation.
- [ ] 22.3 Add negative tamper/stale/missing/path tests.
- [ ] 22.4 Add historical non-visual manifest/bundle compatibility tests.
- [ ] 22.5 Run Debug/Release solution builds.
- [ ] 22.6 Run Debug/Release unit tests.
- [ ] 22.7 Build ValidationDriver/GuineaPig Release.
- [ ] 22.8 Run ValidationDriver self-tests/catalog validation.
- [ ] 22.9 Run `scripts/validate.ps1 -Configuration Release -Ci -Publish`.
- [ ] 22.10 Run strict OpenSpec validation.

## 23. Supervised acceptance campaign

- [ ] 23.1 Use an exclusive supervised Windows desktop and exact candidate identity.
- [ ] 23.2 Run one controlled healthy scenario with `checkpoints` evidence.
- [ ] 23.3 Run one seeded controlled visual defect and retain its packet.
- [ ] 23.4 Run one transient seeded/safe failure with `flight` evidence and confirm pre-failure frames flush.
- [ ] 23.5 Have a capable multimodal development agent actually open the images and produce a valid review result.
- [ ] 23.6 Confirm the agent catches the seeded defect and does not falsely classify the healthy packet as defective.
- [ ] 23.7 Confirm a non-vision path yields `REVIEW_UNAVAILABLE` honestly.
- [ ] 23.8 Confirm image/review hashes verify offline.
- [ ] 23.9 Confirm an intentionally tampered screenshot invalidates the review/evidence.
- [ ] 23.10 Confirm a visual finding cannot bypass failed lease/native prerequisites.

## 24. Documentation and handoff

- [ ] 24.1 Update `docs/TESTING.md` with visual evidence modes, artifact layout, privacy boundary, ring-buffer behavior, AI review workflow, and qualification semantics.
- [ ] 24.2 Update qualification-control-plane/release docs for visual artifact indexing and offline verification.
- [ ] 24.3 Add the canonical `.agent/workflows/visual-evidence-review.md` instructions and synchronize harness adapters through the existing generator if needed.
- [ ] 24.4 Update `.agent/STATE.md` with implementation status, measured limits, schemas, validation, and remaining blockers.
- [ ] 24.5 Reconcile the active physical-certification change only with factual integration notes; do not conflate visual infrastructure completion with physical field-certification completion.
- [ ] 24.6 Sync/archive this OpenSpec change only after the acceptance boundary in `proposal.md` and `design.md` is satisfied.
- [ ] 24.7 Commit/push using the repository's normal authority rules and verify a clean final worktree and exact remote SHA.
