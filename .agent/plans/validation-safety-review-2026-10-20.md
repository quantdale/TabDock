# Plan: Review correction and validation-storage safety implementation

**Status:** complete — changes requested; review only
**Updated:** 2026-10-21

## Objective

Review the new A01/A04 corrections and A10/A02 safety implementations against current source/specs and verify the claimed evidence without implementing fixes.

## Scope and constraints

- Baseline HEAD/main/origin-main: eafe922d925b54001d35886f3807e1312cdb16cd; relevant work is uncommitted. Preserve prior motion/configuration and implementation changes.
- A01/A04/A10/A02 tasks currently all checked; other seven changes remain unimplemented.
- Root/proof/cleanup boundaries need adversarial actual-code fixtures before process validation is accepted. Do not run real application UI, native rescue/input, or full validation against user storage.
- Use disposable fake production/validation roots, actual resolver/helper methods, safe fake probe executables and selected AST-extracted PowerShell functions for repros.
- Capture relevant source/test hashes before review and recheck at handoff. Only review records/checkpoint may change.

## Steps

- [x] Reconcile current Git, task status, spec contracts and correction source.
- [x] Read resolver, child-proof, cleanup, launcher and read-only discovery paths.
- [x] Verify A01/A04 corrections and run fresh targeted safe builds/tests.
- [x] Reproduce root-alias/refusal, proof/cleanup ownership and probe-budget weaknesses in disposable fixtures.
- [x] Inspect relevant test/gate evidence; distinguish passing examples from safety guarantees.
- [x] Write findings and correction prompt, verify source unchanged and update checkpoint.

## Evidence and decisions

- Current implementation adds ProductStorage and ValidationStorageProof, moves pending hygiene to explicit mutation paths and routes default storage roles.
- Confirmed six safety/qualification findings: resolver accepts extended production aliases and blank-pair fallback (S1/P1); script outer cleanup bypasses lost-owner retention on probe failure (S2/P1); cleanup accepts incomplete/cross-root markers (S3/P2); unsupported/nonvalidation proof accepted (S4/P2); stdout/stderr reads bypass timeout (S5/P2); global test override breaks unrelated test interleaving (S6/P2).
- Fresh Debug/Release targeted tests 122/122 each and driver storage tests 11/11 pass, demonstrating missing negative coverage rather than absence of defects. Forced Release rebuild succeeds with two xUnit1031 warnings (unexpected-load test blocking waits).
- Old A01/A04 corrections and unexpected-load guard coverage verified; no new A10 read-only defect reproduced. Full unit/process/UI gates not rerun under incomplete A02 safety guarantees.
- Report: `.agent/investigations/validation-safety-review-2026-10-21.md`; correction prompt: `.agent/plans/validation-safety-followup-2026-10-21.md`.
- All 29 fingerprinted implementation/test/doc/config inputs unchanged; no source, checkbox, commit or push changes. Local evidence/probe archive is ignored under `artifacts/validation-safety-review-2026-10-20/`.

## Handoff

**Next action:** Implementation session fixes S1–S6 under A02 and repairs warning/test-isolation evidence before requalifying dependent full gates; retain working A01/A04/A10 fixes.
**Blockers:** No review blocker. A02 cannot receive unconditional safety acceptance until its reproduced refusal/cleanup/budget defects are resolved.
