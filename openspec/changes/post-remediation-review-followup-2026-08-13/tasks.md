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
