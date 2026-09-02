# Real-app hardening acceptance matrix — 2026-09-02

**Historical candidate (matrix baseline):** `6223cbffa44072149cbe1496bb9d5b635996f0b1`
**Executable:** `B445BF472FCD5F1349CA2E7B2E3C6BEEE85922A31F5ECEAB753E82C2D9ED5E35` (`1.1.0+6223cbf`)
**Driver:** `0BAFC906EEC34880437BCAF85567ED14A52631A170ACF56876994AB842D44618`
**Snapshot:** `92790d2a` (virtual 3840x1200, 120+96 DPI)

**FINAL CORRECTIVE CANDIDATE (2026-09-02):** `fbc4d926abf34e1ace4260ddef25807699f402b9`
**Executable:** `BFC7EF3445020EC920A1BAA41C4B697AEDC306541FBCF03D8CCA9AD024059851` (`1.1.0+fbc4d92`)
**Driver:** `781FF394A77A9828F687D66276DD9E16A059D4DE7B4CE1E440B30AC3F5C1BEC0`
**Snapshot:** `92790d2a` (virtual 3840x1200, 120+96 DPI)

## Summary

- **Chrome:** PASS 2/2 cycles (run a9f9a151 and fd0dd8b5 visual) — native containment proven, drift 1/2, style 0x16CF→0x160B, brightness 203, one F11 request; **final candidate fbc4d92: PASS 2/2 (run 6a5bb064) with restricted visual packet `c03de022…dcfe`, operator review `VISUAL_SUSPECT` (benign container-fullscreen capture artifact), verifier `Valid:true`**
- **Edge:** FLAKE_UNCLASSIFIED — first invocation FAIL second cycle, immediate rerun PASS both cycles (run ba1cedc3 FAIL, 35525ad6 PASS); **terminal classification at final candidate: `PROVEN_EXTERNAL_BROWSER_INPUT_FLAKE`** (5×3 characterization 15/15 PASS at fbc4d92: runs 928524b2, f0117322, 7b8adc23, 08a14221, b3d9464c; setup-only FAIL_HARNESS 8cfbc878 preserved; historical mechanism reconstructed from TabDock log — one posted F11 exit not consumed by Edge before the 3500ms settle, no product/repair change); **final candidate visual packet (run 76fb0a68) `1a47df5a…a019`, operator review `VISUAL_SUSPECT` (same benign artifact; no account email/sync prompt — privacy fix confirmed), verifier `Valid:true`**
- **Brave:** PASS 2/2 (run 74745302); **final candidate fbc4d92: PASS 2/2 (run b151a8ce) with restricted visual packet `75c6c51c…c652`, operator review `VISUAL_OK`, verifier `Valid:true`**
- **Firefox:** SKIP_CAPABILITY (not installed)
- **Notepad:** BLOCKED_ENVIRONMENT — Windows 11 single-instance broker (PID 100564 reused, orphan windows 0x352C64/0x82CCC, visual monitor binding failed)
- **Windows Terminal:** BLOCKED_CAPABILITY — launcher vs monarch host inspection pending, maximize-repro blocked due to visual policy none

**Tamper check (real packet):** Edge packet copied to temp root, exactly one byte of `edge-normal-f11-1-fullscreen/0005-GUEST_WINDOW.png` flipped (`37201d69…`→`07fa1ab8…`), offline verifier deterministically rejected with `visual evidence hash mismatch` (manifest + packet + review bindings); authoritative packet untouched.

See JSON for full per-scenario topology/lease/foreground/point/packet/cleanup details.

## Privacy/Visual

- Chrome/Brave run-owned fresh profile → TEST_OWNED host+guest crop; REAL_APP_RESTRICTED reserved for adopted (Notepad/Terminal)
- Whole-desktop disabled, packet hash-bound, verifier Valid:true where applied
- 19.1: **COMPLETED_AND_PROVEN at final candidate fbc4d92** — Chrome/Edge/Brave each have a restricted packet (baseline/before/fullscreen/after, 14 PNGs, TEST_OWNED, AllowVirtualDesktop=false, topology-bound), offline verifier `Valid:true` on all three packet+result pairs, operator multimodal review completed (SUSPECT-benign/SUSPECT-benign/OK), real-packet tamper rejection proven; native PASS is authoritative
- 19.4: COMPLETED_AND_PROVEN for policy enforcement, BLOCKED_CAPABILITY for adopted Notepad privacy-safe capture (cannot isolate unrelated tabs without tab-aware filtering)

## First-failure authority

Preserved: Edge FAIL cycle 2 retained even though rerun PASS (FLAKE_UNCLASSIFIED → terminal PROVEN_EXTERNAL_BROWSER_INPUT_FLAKE at final closure, not best-of-N PASS); Notepad orphan failure retained.

## Product-repair gate

No production TabDock behavior edited from real-app runs; Edge flake is external browser input, not stable FAIL_PRODUCT. Forbidden repairs not introduced.

## Deterministic gates at matrix time

812/812 Debug/Release, 173/173 selftest, 179/179 release-tooling, catalog 135, strict OpenSpec 39/39 valid, snapshot 92790d2a, validate.ps1 -Ci -Publish exit 0, native ABI PASS, resource-headless PASS
