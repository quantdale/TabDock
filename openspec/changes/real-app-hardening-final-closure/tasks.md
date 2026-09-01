# Tasks — real-app-hardening-final-closure

## 0. Authority and baseline

- [ ] 0.1 Resolve Git truth dynamically (fetch, status, branch, HEAD, origin/main, worktrees, remote, GitHub). Classify any divergence before mutation. Never reset/clean user work.
- [ ] 0.2 Read `AGENTS.md`, `.agent/STATE.md`, `docs/ARCHITECTURE.md`, `docs/TESTING.md`, `openspec/config.yaml`, the archived real-app/DPI/visual campaigns, the real-app handoff, the acceptance matrices, the DPI repair provenance, the visual-evidence-review workflow, and the current implementations listed in the campaign brief.
- [ ] 0.3 Create the durable corrective ledger record `.agent/investigations/real-app-hardening-final-closure-2026-09-02.md` mapping prior archived tasks 1.7, 4.3, 7.1 to `REOPENED_FOR_CORRECTIVE_CLOSURE` plus `EDGE_FIRST_VALID_FAIL_PRODUCT_DISPOSITION`, and recording 38 checkbox rows (not 26).
- [ ] 0.4 Run strict OpenSpec validation on this change BEFORE implementation and record the result.

## 1. Edge characterization and disposition

- [ ] 1.1 Record the historical Edge evidence: first invocation `FAIL_PRODUCT` on second F11 cycle (run `ba1cedc3`), immediate rerun PASS (run `35525ad6`), current disposition `FLAKE_UNCLASSIFIED`; frozen as authoritative.
- [ ] 1.2 Run the bounded Edge characterization matrix: 5 independent invocations × 3 F11 enter/exit cycles = 15 cycles, fresh isolated Edge profile per invocation, exact committed candidate, exact Release ValidationDriver, exclusive supervised desktop, valid lease, topology snapshot.
- [ ] 1.3 Record every per-cycle field: run ID, invocation index, cycle index, PID/start, HWND, process generation, pane rect, before/fullscreen/after rects, style before/during/after, IsZoomed, monitor, DPI, LOCATIONCHANGE/drift count, `SHEPHERD[presentation-restore-request]` count, F11-exit-to-settle time, IsDocked, point ownership, foreground, tab membership, visual packet/result, cleanup.
- [ ] 1.4 Classify the historical failure: `PROVEN_PRODUCT_DEFECT` / `PROVEN_HARNESS_DEFECT` / `PROVEN_ENVIRONMENT_FAILURE` / `CHARACTERIZED_PRODUCT_FLAKE` / `NOT_REPRODUCED_BUT_UNEXPLAINED` with a durable rationale.
- [ ] 1.5 If `PROVEN_PRODUCT_DEFECT`: freeze first failure, identify first divergence, update spec if underspecified, add non-vacuous deterministic regression, make the smallest Shepherd-preserving repair, rerun the complete 5×3 characterization, requalify Chrome + Brave adjacency, retain the original `FAIL_PRODUCT` as pre-fix evidence.
- [ ] 1.6 If `PROVEN_HARNESS_DEFECT`: fix the harness only, add a regression, keep the raw original result, reclassify through the durable investigation.
- [ ] 1.7 Record the final Edge disposition in the acceptance matrix and the corrective ledger. If the valid first failure remains unexplained or is a characterized product flake, do NOT archive final closure.
- [ ] 1.8 Apply the product-repair forbidden list: no SetParent, style stripping, permanent topmost, global z-order polling, unbounded SetWindowPos, blind second F11, blind timeout inflation, tab-switch recovery, weak ownership, foreign-process cleanup.

## 2. Chromium visual packet production

- [ ] 2.1 Investigate the prior zero-visual-log root cause (topology binding, monitor identity, privacy-scope policy, target identity, scenario ordering, visual recorder state, capability planning, stale context, or actual capture failure) and record the classification.
- [ ] 2.2 Fix the visual harness only if a harness defect is proven, adding a regression proving a valid controlled browser F11 checkpoint attempt produces the required selected visual artifacts. Do not modify production TabDock for a visual-harness problem.
- [ ] 2.3 For Chrome run at least one accepted exact-candidate physical F11 attempt with `--visual-evidence checkpoints --visual-review-packet`, isolated test-owned controlled content: baseline, before-F11, fullscreen, post-containment/restored checkpoints (plus a settled fifth where useful).
- [ ] 2.4 For Edge run the same accepted packet attempt (if the native first failure remains unresolved, the packet and review are correlated, not overriding).
- [ ] 2.5 For Brave run the same accepted packet attempt.
- [ ] 2.6 Verify each packet: raw PNGs exist, manifest exists, packet exists, packet SHA-256 computed from exact bytes, selected image hashes verify, candidate/run/scenario/attempt/topology/monitor/privacy-class bindings match, verifier returns `Valid:true`.
- [ ] 2.7 Privacy: `TEST_OWNED` isolated profile, no personal history/accounts, smallest approved capture region, `AllowVirtualDesktop=false`, no unrelated windows or desktop.

## 3. Multimodal review and tamper check

- [ ] 3.1 Perform an actual capable multimodal review per `.agent/workflows/visual-evidence-review.md` of each accepted Chromium packet (Chrome, Edge, Brave): contact sheet, every required raw checkpoint, expected visual state, correlated native evidence; hash-bound result file.
- [ ] 3.2 Record verdicts (`VISUAL_OK`/`VISUAL_SUSPECT`/`VISUAL_DEFECT`/`REVIEW_UNAVAILABLE`); required visual acceptance cannot close with `REVIEW_UNAVAILABLE` unless this proposal explicitly permits it; `VISUAL_OK` cannot override a native failure.
- [ ] 3.3 Run the tamper check: copy one accepted packet to a temporary root, alter one PNG byte, run the offline verifier, require deterministic rejection, record the exact rejection reason; the authoritative packet is untouched.

## 4. Canonical final gates

- [ ] 4.1 Run `scripts/validate.ps1 -Configuration Release -Ci -Publish` (exact canonical invocation; no substitutes). Record exit code, candidate, build/test results, publish smoke, dependency audit, privacy, recovery, support bundle, resource result, OpenSpec result.
- [ ] 4.2 Run the explicit native ABI gate `TabDock.exe --selftest-native-abi` (exact final Release executable) — PASS required, not inferred from compilation.
- [ ] 4.3 Run the deterministic resource-headless gate covering all declared profiles with the canonical seed/cycle count (`--cycles 32 --profile all --seed 20260824`), recording run ID, sample count, profiles, handle/USER/GDI/memory/thread results, artifacts, PASS/FAIL/BLOCKED.
- [ ] 4.4 Run the canonical privacy/recovery/support gates: support-bundle privacy, recovery, pending-recovery, doctor/version, visual privacy, historical bundle compatibility — actually executed.

## 5. Ledger reconciliation and matrix update

- [ ] 5.1 Record in the corrective investigation that the archived real-app ledger contains 38 checkbox rows and that the earlier "26 tasks" report was incorrect; do not delete or renumber archived historical tasks.
- [ ] 5.2 Complete the corrective mapping table for 1.7, 4.3, 7.1 and `EDGE_FIRST_VALID_FAIL_PRODUCT_DISPOSITION` with final evidence and `SATISFIED_POST_ARCHIVE_BY_FINAL_CLOSURE` where satisfied.
- [ ] 5.3 Update the durable acceptance matrices `.agent/investigations/real-app-hardening-acceptance-matrix-2026-09-02.json` and `.md` for Chrome/Edge/Brave native+visual+packet+review+cleanup, preserving first-attempt history and Notepad/Terminal/Firefox blocks.

## 6. Final validation, candidate boundary, and archive

- [ ] 6.1 After all source/test/harness changes settle, commit them and record `FINAL_CANDIDATE_SHA`; build the exact Release executable and driver from that committed SHA; record exe/driver SHA-256, version, informational version, signing status, release mode, production eligibility.
- [ ] 6.2 Run the full final deterministic validation: `dotnet build` Debug/Release, `dotnet test` Debug/Release, build ValidationDriver/GuineaPig Release, selftest all, capability, visual, catalog, plan release, plan real-app, topology lab, visual verifier, real-app visual packet verifier, tamper, native ABI, resource lifecycle, privacy/recovery, historical compatibility, release-tooling, strict OpenSpec, canonical `validate -Ci -Publish` — record current counts dynamically (do not reuse stale counts).
- [ ] 6.3 Run strict OpenSpec validation on this change and (`--specs`) on all canonical specs; archive only when every closure condition holds.
- [ ] 6.4 If any closure condition fails, keep this change ACTIVE and report exactly what remains; do not archive.
- [ ] 6.5 Final git settlement: commit bounded closure records (never screenshots/run roots/bin/obj/publish/user paths/profile data/credentials/logs), push main normally, prove `HEAD == origin/main`, main-only, clean worktree, no open PRs, no active changes.
- [ ] 6.6 Produce the final closure report covering git authority, corrective OpenSpec, ledger correction, Edge, Chrome, Edge visual, Brave, visual privacy, rows 19.1/19.4, final validation, final candidate, remaining external limitations, and final proof.