# Tasks — visual evidence closure and performance re-qualification

## 1. Campaign authority and provenance

- [x] 1.1 Re-read the predecessor visual-evidence change, its five blocked acceptance tasks, current `.agent/STATE.md`, and the next-campaign investigation; record the exact starting candidate/run context.
- [x] 1.2 Inventory modified/untracked visual source, test, spec, workflow, and investigation files; classify each as intended, generated run output, unrelated user work, or unresolved before any archive.
- [x] 1.3 Define the tracked-file allowlist and generated-artifact exclusion for the predecessor closure; prove `.visual-validation-runs`, build output, logs, caches, screenshots, and secrets cannot enter Git.
- [x] 1.4 Keep production TabDock, Shepherd/no-reparent behavior, native identity/lease/foreground rules, and existing pixel metric semantics unchanged; document any required validation-only seam.

## 2. Milestone A — verifier and contract closure

- [x] 2.1 Trace every `packetSha256` writer, serializer, verifier branch, bundle index, and fixture; classify the `packetSha256: string.Empty` literal and document the exact failing invariant before changing it.
- [x] 2.2 Implement and test one computed-packet-hash invariant joining packet bytes, review result, visual manifest, run hierarchy, candidate, run, scenario, and attempt; reject empty placeholders and caller-substituted hashes.
- [x] 2.3 Replace nullable/default required visual collections with strict current-schema constructors/converters and deterministic missing/null/duplicate-element validation; retain explicit historical non-visual migration behavior.
- [x] 2.4 Add a schema-versioned authoritative derived-artifact-failure record for contact-sheet failure and propagate its ID, reason, requiredness, raw-preservation state, and binding to scenario result, visual manifest, packet, and offline verifier.
- [x] 2.5 Add deterministic negative coverage for wrong/empty packet hashes, changed packet bytes, missing/null collections, stale identity, tampered derived-failure records, contact failure with intact raw PNGs, and unacknowledged failure preventing visual PASS.

## 3. Milestone A — supervised visual acceptance

- [x] 3.1 Build one exact clean candidate and run a healthy controlled visual checkpoint packet on an exclusive supervised desktop with lease, identity, topology, and candidate evidence. **Proven by b19e33e exact candidate, run `96000be6-32c5-4fd4-860f-fc31bca5cae6`, native `PASS`, lease `true`, packet SHA `daf8a966725e0fdd429d13e495fcb9d26c0139b8d9f2ec1430fb0901dea75633`.**
- [x] 3.2 Produce one test-owned seeded visual-defect packet without defect-disclosing filenames/prompts; retain the first-attempt raw images and have a capable multimodal agent identify the defect. **Proven by run `c41e4302-b336-40f3-9006-9b500da8d010`; attempt 1 retained the red seeded guest and was reviewed `VISUAL_DEFECT`.**
- [x] 3.3 Produce one transient/flight-recorder failure packet; verify ordered pre-failure frames, trigger-frame retention, bounded history, and cleanup after flush. **Proven by run `3071f78b-7988-4c35-95e0-b4c96dd23a52`; three failure artifacts, two pre-trigger frames, ring count zero, native/lease `PASS`/`true`.**
- [x] 3.4 Have the capable reviewer emit valid hash-bound results for the healthy, defective, and flight packets; prove healthy imagery is not falsely flagged and the seeded defect is detected. **Proven by strict verifier: healthy `VISUAL_OK`, defect attempt 1 `VISUAL_DEFECT`, rerun `VISUAL_OK`, flight `VISUAL_OK`; all exact b19 packet/result bindings validate.**
- [x] 3.5 Run the non-vision path and record `REVIEW_UNAVAILABLE`; verify required-review gates remain non-pass while optional native qualification stays independently classified. **Proven by run `d07e2795-9f05-4cbd-91ca-b8dd9d628e1b`, required gate `BLOCKED_CAPABILITY`, empty review collections, and native packet `PASS`/lease `true`.**
- [x] 3.6 Mutate screenshot bytes, packet/result hashes, paths, candidate/scenario bindings, and native lease evidence; prove offline verification rejects each mutation and visual evidence cannot override native failure. **Exact b19 screenshot tamper was rejected with hash mismatches; gate reflection preserved `FAIL_HARNESS`/`BLOCKED_SUPERVISED` under `VISUAL_OK` and rejected required defect/unavailable.**
- [ ] 3.7 Execute the predecessor change's remaining acceptance tasks, preserve first-attempt visual defects across reruns, reconcile intended files into Git, synchronize canonical specs, and archive the predecessor only after its acceptance boundary is satisfied.

## 4. Milestone B — measurement harness and data collection

- [x] 4.1 Add validation-side measurement records for mode, scenario, candidate, machine/topology, dimensions, capture method, sample number, timing phases, retained frames/bytes, allocations, CPU, and native/resource observations without changing production behavior.
- [x] 4.2 Collect paired visual-disabled baseline samples for deterministic/headless resource profiles and establish exact zero visual-work counters plus separately reported policy-branch overhead.
- [x] 4.3 Collect repeated checkpoint-mode samples for rename, split, inline capture, maximize/fullscreen, title centering, and one controlled topmost/high-risk transition, including capture, PNG, write/hash, manifest, contact-sheet, and packet costs.
- [x] 4.4 Collect repeated flight-mode healthy-discard and failure-flush samples, including cadence, ring occupancy/eviction, peak memory, flush cost, retained bytes, cancellation/timeout, and post-cleanup state.
- [x] 4.5 Observe GDI/HBITMAP/HDC, USER, process/file handles, private bytes, working set, threads/workers, timers, TabDock-owned windows, artifact residue, and ring memory before/after each paired lifecycle; block on unavailable required observations.
- [x] 4.6 Emit a portable measurement report with raw samples plus per-cell sample count, median, p95 when supported, maximum, units, outlier notes, exact candidate SHA, and synthetic/physical classification.

## 5. Milestone B — budgets and regression gates

- [x] 5.1 Derive conservative per-mode/scenario latency, bytes, count, memory, allocation, CPU, native-resource, artifact-growth, and cleanup budgets from the measured report with documented statistics, margin, and provenance.
- [x] 5.2 Add deterministic disabled-mode regression gates proving zero capture/encode/retention/contact/packet/worker/timer/artifact work and rejecting missing metrics disguised as zero.
- [x] 5.3 Add deterministic synthetic gates for hard dimensions/count/bytes/ring limits, measured timing/size budgets, contact/packet limits, healthy discard, and bounded failure flush.
- [x] 5.4 Extend resource-lifecycle qualification to compare visual-disabled and visual-enabled cells and fail/block on handle/GDI/file/timer/worker leaks, unbounded memory/artifact growth, or unexplained baseline regression.
- [x] 5.5 Exercise success, required/optional capture failure, encode/contact/packet failure, cancellation, timeout, abort, healthy discard, and failure flush; prove recorder stop, native cleanup, temp-file cleanup, and first-attempt evidence preservation.
## 6. Milestone E — exact Release v1.1 closure
> **Pre-final evidence correction (2026-09-01).** The prior `ef9fe35` execution
> evidence for 6.2–6.5 is preserved in
> `.agent/investigations/release-semantics-correction-2026-09-01.md`, but it is
> not final closure evidence. These rows remain pending until the final
> post-A/archive/metadata clean tree is qualified. Task 6.3's exact-candidate
> build MUST be rerun after supervised A, predecessor disposition/archive, and
> all final metadata/spec commits settle; the `7f4b9df` task-ledger commit has
> no claimed exact binary candidate.
- [ ] 6.2 Run Debug/Release solution builds, unit tests, ValidationDriver/GuineaPig deterministic self-tests, catalog/plan checks, visual verifier gates, and historical non-visual manifest/bundle checks. **Pre-final `ef9fe35` evidence retained; final rerun pending.**
- [ ] 6.3 From the final clean committed tree, build the Release v1.1 candidate once; verify requested SHA equals `HEAD`, embedded source identity equals SHA, and record a fresh executable SHA-256 without reusing the historical artifact hash. **The pre-final `ef9fe35` candidate evidence does not satisfy this row.**
- [ ] 6.4 Run canonical `scripts/validate.ps1 -Configuration Release -Ci -Publish`, dependency vulnerability audit, strict OpenSpec validation, version/publish smoke, resource/privacy/ABI checks, and exact artifact/hash re-verification. **Pre-final `ef9fe35` evidence retained; final rerun pending.**
- [ ] 6.5 Preserve blocked signing, physical, external, and capability states honestly; reject any release claim whose visual/performance evidence is stale, missing, tampered, synthetic-only for a physical gate, or unproven by measured budgets. **Pre-final `ef9fe35` evidence retained; final truthfulness record pending.**
- [ ] 6.6 Obtain acceptance, synchronize/archive this change only after A, B, and E are complete, commit/push through the repository's authorized mainline workflow, and independently verify final SHA, remote equality, and clean worktree.

- [x] 7.1 Record handoff candidates for separate DPI/topology hardening (negative/above-origin monitors, 150%–200% DPI, deeper mixed-DPI/topmost/transfer coverage) and real-app hardening (Chromium fullscreen breadth, Notepad broker, Windows Terminal hosting, adopted-app lifecycle quirks); do not implement either here.

