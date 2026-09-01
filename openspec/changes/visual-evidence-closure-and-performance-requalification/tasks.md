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

- [ ] 3.1 Build one exact clean candidate and run a healthy controlled visual checkpoint packet on an exclusive supervised desktop with lease, identity, topology, and candidate evidence.
- [ ] 3.2 Produce one test-owned seeded visual-defect packet without defect-disclosing filenames/prompts; retain the first-attempt raw images and have a capable multimodal agent identify the defect.
- [ ] 3.3 Produce one transient/flight-recorder failure packet; verify ordered pre-failure frames, trigger-frame retention, bounded history, and cleanup after flush.
- [ ] 3.4 Have the capable reviewer emit valid hash-bound results for the healthy, defective, and flight packets; prove healthy imagery is not falsely flagged and the seeded defect is detected.
- [ ] 3.5 Run the non-vision path and record `REVIEW_UNAVAILABLE`; verify required-review gates remain non-pass while optional native qualification stays independently classified.
- [ ] 3.6 Mutate screenshot bytes, packet/result hashes, paths, candidate/scenario bindings, and native lease evidence; prove offline verification rejects each mutation and visual evidence cannot override native failure.
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
- [x] 6.1 Integrate visual manifests, review packet/result bindings, performance reports, selected budget provenance, and historical compatibility into the qualification hierarchy and offline bundle index.
- [ ] 6.2 Run Debug/Release solution builds, unit tests, ValidationDriver/GuineaPig deterministic self-tests, catalog/plan checks, visual verifier gates, and historical non-visual manifest/bundle checks.
- [ ] 6.3 From the final clean committed tree, build the Release v1.1 candidate once; verify requested SHA equals `HEAD`, embedded source identity equals SHA, and record a fresh executable SHA-256 without reusing the historical artifact hash.
- [ ] 6.4 Run canonical `scripts/validate.ps1 -Configuration Release -Ci -Publish`, dependency vulnerability audit, strict OpenSpec validation, version/publish smoke, resource/privacy/ABI checks, and exact artifact/hash re-verification.
- [ ] 6.5 Preserve blocked signing, physical, external, and capability states honestly; reject any release claim whose visual/performance evidence is stale, missing, tampered, synthetic-only for a physical gate, or unproven by measured budgets.
- [ ] 6.6 Obtain acceptance, synchronize/archive this change only after A, B, and E are complete, commit/push through the repository's authorized mainline workflow, and independently verify final SHA, remote equality, and clean worktree.

- [x] 7.1 Record handoff candidates for separate DPI/topology hardening (negative/above-origin monitors, 150%–200% DPI, deeper mixed-DPI/topmost/transfer coverage) and real-app hardening (Chromium fullscreen breadth, Notepad broker, Windows Terminal hosting, adopted-app lifecycle quirks); do not implement either here.

