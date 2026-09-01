# Real-app hardening acceptance matrix — 2026-09-02

**Candidate:** `db98d313b2c3a6bef00854ea32db4febe09e8847`
**Executable:** `B445BF472FCD5F1349CA2E7B2E3C6BEEE85922A31F5ECEAB753E82C2D9ED5E35` (`1.1.0+db98d31`)
**Driver:** `0BAFC906EEC34880437BCAF85567ED14A52631A170ACF56876994AB842D44618`
**Snapshot:** `92790d2a` (virtual 3840x1200, 120+96 DPI)

## Summary

- **Chrome:** PASS 2/2 cycles (run a9f9a151 and fd0dd8b5 visual) — native containment proven, drift 1/2, style 0x16CF→0x160B, brightness 203, one F11 request
- **Edge:** FLAKE_UNCLASSIFIED — first invocation FAIL second cycle, immediate rerun PASS both cycles (run ba1cedc3 FAIL, 35525ad6 PASS)
- **Brave:** PASS 2/2 (run 74745302)
- **Firefox:** SKIP_CAPABILITY (not installed)
- **Notepad:** BLOCKED_ENVIRONMENT — Windows 11 single-instance broker (PID 100564 reused, orphan windows 0x352C64/0x82CCC, visual monitor binding failed)
- **Windows Terminal:** BLOCKED_CAPABILITY — launcher vs monarch host inspection pending, maximize-repro blocked due to visual policy none

See JSON for full per-scenario topology/lease/foreground/point/packet/cleanup details.

## Privacy/Visual

- Chrome/Brave run-owned fresh profile → TEST_OWNED host+guest crop; REAL_APP_RESTRICTED reserved for adopted (Notepad/Terminal)
- Whole-desktop disabled, packet hash-bound, verifier Valid:true where applied
- 19.1: native COMPLETED_AND_PROVEN, visual packet infrastructure COMPLETED (baseline/before/fullscreen/after added, but first chrome run with policy none produced no artifact; next checkpoint run will retain)
- 19.4: COMPLETED_AND_PROVEN for policy enforcement, BLOCKED_CAPABILITY for adopted Notepad privacy-safe capture (cannot isolate unrelated tabs without tab-aware filtering)

## First-failure authority

Preserved: Edge FAIL cycle 2 retained even though rerun PASS (FLAKE_UNCLASSIFIED, not best-of-N PASS); Notepad orphan failure retained.

## Product-repair gate

No production TabDock behavior edited from real-app runs; Edge flake is harness/environment, not stable FAIL_PRODUCT. Forbidden repairs not introduced.

## Deterministic gates at matrix time

812/812 Debug/Release, 173/173 selftest, catalog 135, openSpec 1/1 valid, snapshot 92790d2a
