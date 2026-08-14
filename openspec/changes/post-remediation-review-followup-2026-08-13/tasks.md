## 1. Durable state and specifications

- [x] 1.1 Record the R3 Git-authority/self-reference convention in canonical agent instructions and compact `.agent/STATE.md`.
- [x] 1.2 Add and validate the follow-up native identity, ShowWindow post-state, and monitor-DPI capability specs.

## 2. Process-instance identity

- [x] 2.1 Add native process-start probing and a two-tier identity gate with captured-object/HWND generation binding.
- [x] 2.2 Apply strong identity to destructive, delayed, foreground, release, and recovery paths while preserving hot-path bounds.
- [x] 2.3 Add deterministic identity fixtures and update ValidationDriver start-time checks to fail closed.

## 3. ShowWindow semantics

- [x] 3.1 Replace restore BOOL interpretation with post-state verification and route delayed minimize restore through the Shepherd gate.
- [x] 3.2 Add deterministic regression coverage and document the supervised hidden/minimized/visible/hide/release cases.

## 4. DPI contract

- [x] 4.1 Replace `GetDpiForMonitor` with the hidden PMv2 helper-window `GetDpiForWindow` probe and update all production/driver call sites.
- [x] 4.2 Add deterministic probe/conversion tests and update architecture/testing documentation with the official API contract.

## 5. Validation and handoff

- [x] 5.1 Run targeted builds/self-tests and the canonical Release/OpenSpec/diff gates.
- [x] 5.2 Inspect the complete diff, create one coherent commit, push fast-forward to `origin/main`, and verify hosted CI without a state-only follow-up commit.


## 6. Post-remediation safety closure

- [x] 6.1 Separate identity match, positive mismatch, and unverifiable probe
  outcomes; make release transaction ownership preserve members and journals
  on uncertainty.
- [x] 6.2 Bump the crash-journal schema to v3, preserve literal historical v1/v2
  evidence as pending manual-recovery data, and retain future versions.
- [x] 6.3 Complete visible/non-iconic/non-zoomed SW_RESTORE post-state checks
  and deterministic failure fixtures.
- [x] 6.4 Complete the final state-protocol, CI-runtime, R4/R6 review, and
  canonical validation gate; query hosted CI dynamically after the substantive
  content commit without a state-only follow-up.

## 7. R12–R15 final source-hardening pass

- [x] 7.1 Add immediate mutation-boundary revalidation to capture, hide,
  release, rescue, and affected hot-path sequencing; add deterministic TOCTOU
  tests and preserve bounded hot-tier identity behavior.
- [x] 7.2 Implement read-only pending evidence discovery and explicitly
  supervised v1/v2 recovery with candidate confirmation, a distinct temporary
  generation token, safe partial-transaction behavior, and entry-scoped
  durable retirement; add privacy and lifecycle tests.
- [x] 7.3 Reconcile the known-DPI-unaware acceptance contract across source
  comments, README, architecture, testing, main specs, and deterministic DPI
  tests without claiming physical mixed-DPI qualification.
- [x] 7.4 Install pinned OpenSpec in CI with `--ignore-scripts`, document the
  reviewed postinstall rationale, reproduce local validation, and inspect the
  hosted warning state.
- [x] 7.5 Run the complete canonical qualification suite, review every changed
  file and invariant, commit one coherent substantive unit, push main, and
  verify/fix the exact hosted CI run.
