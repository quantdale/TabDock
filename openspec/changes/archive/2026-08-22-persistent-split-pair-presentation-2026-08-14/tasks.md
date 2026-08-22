## Implementation

- [x] Add relationship-defined versus pair-presented runtime state and
      centralized suspend/resume/exit transitions with journal-safe fail-closed
      behavior.
- [x] Audit all relayout, constraints, z-order, activation, minimize/restore,
      move/size, and settle callers for presentation-state correctness.
- [x] Retain the composite projection while dormant and keep LEFT/RIGHT
      identity stable across resume.
- [x] Suppress `Split screen` for current pair members while retaining
      `Exit split screen` in presented and dormant menus.
- [x] Extend logical diagnostics and native expected-geometry interpretation to
      distinguish relationship from presentation.

## Regression coverage

- [x] Replace old third-tab-exits expectations with suspend/resume coverage,
      including >=20 cycles and four-tab C/D switching.
- [x] Add dormant explicit-exit, dormant member-removal, and diagnostic-state
      assertions.
- [x] Add presented/dormant paired-member UI Automation menu assertions.
- [x] Preserve hover, direct-focus, structural-removal, and settle regressions.
- [x] Add controlled rendering/client-response qualification and isolate browser
      evidence outside the repository.
- [x] Add per-run process/HWND provenance, root-at-point diagnostics, stale HWND
      rejection, and fail-closed guarded input coverage.
- [x] Add JSON/JUnit qualification artifacts and one tiered split orchestrator
      covering deterministic, controlled, interactive, browser, stress, and
      historical comparison modes.

## Qualification

- [x] Run Release build and `scripts/validate.ps1 -Configuration Release -Ci -Publish`.
- [x] Run focused guarded interactive scenarios and available browser matrix.
- [x] Compare baseline `8b75c99cdd149648b54f98ed2ff0f9f2598bd0fc` with candidate.
- [x] Audit diff/prohibited APIs, commit/push main, and verify exact-SHA CI.
      (Verified 2026-08-22: implementation commits 13c3d6f/ace3161 are on
      origin/main; every pushed SHA since has been qualified by hosted CI.)
