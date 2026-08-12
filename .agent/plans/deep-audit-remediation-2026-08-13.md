# Deep-audit remediation plan — 2026-08-13

## Objective

Resolve the current-HEAD versions of the 2026-08-12 whole-codebase audit
findings, complete the focused Win32 contract audit, preserve Shepherd,
persistence, journal, and WinEvent invariants, and leave an honest release
qualification record on a non-main branch.

## Workstream status

- [x] Re-read repository instructions, state, waypoints, OpenSpec, testing
      guidance, workflow, and recent history.
- [x] Re-verify F1-F11 against current source rather than applying the old
      baseline blindly.
- [x] Fix HDWP ABI/chaining/fallback, ShowWindow semantics, mutable-title
      identity, DWM mutation, embedded path redaction, OS labeling, and
      per-user instance scope.
- [x] Add deterministic self-tests, mutable-title harness coverage, privacy
      bundle scanning, and hosted CI gates.
- [x] Correct the additional `WINDOWPLACEMENT` struct/ref contract defect and
      record all interop findings in `docs/internal/win32-interop-audit-2026-08-13.md`.
- [x] Run the complete build, release/publish, CLI, doctor/support-bundle,
      OpenSpec, script, diff, and privacy validation matrix.
- [x] Synchronize durable state/tasks/docs with the observed targeted
      supervised runs and the remaining human qualification gate.
- [x] Inspect the complete diff and final validation output.
- [ ] Commit the coherent remediation on this branch; push/draft PR only if
      credentials and repository workflow allow it.

## Explicit non-claims

Targeted supervised ValidationDriver runs are recorded in the current state
file and passed: mutable-title capture, DWM no-mutation, split entry/direct
input/native re-glue/minimize-restore, and the three startup visibility guards.
Human visual startup/z-order acceptance, mixed-DPI hardware qualification,
browser/real-app coverage, crash rescue, and external-machine reproduction are
not complete. `ContainerWindow` remains a deliberate follow-up extraction
candidate, not a line-count-only refactor in this remediation.
