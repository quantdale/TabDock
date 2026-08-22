## 1. OpenSpec and identity foundation

- [x] 1.1 Validate the proposal/spec/design shape and keep this task list synchronized with the implementation.
- [x] 1.2 Add generated MSBuild/assembly metadata and an authoritative `BuildIdentity` API with graceful unavailable values, stable commit parsing, configuration/RID, and no timestamp entropy.
- [x] 1.3 Add early argument dispatch and UI-free `--version`, including redirected/parent-console output and explicit executable hash only in command mode.

## 2. Structured diagnostics and snapshots

- [x] 2.1 Extend environment/monitor helpers into structured, safe machine, monitor/DPI, GPU, elevation, and persistence summaries without mutating state.
- [x] 2.2 Add privacy-safe native HWND observation with process identity, geometry, visibility/minimize/z-order/foreground/DPI/DWM fields and WindowFromPoint probes; tolerate destroyed/access-denied windows.
- [x] 2.3 Add reusable logical presentation snapshot models and an in-process provider for every live container/group, preserving observed-versus-desired separation.

## 3. Trace and support export

- [x] 3.1 Add a bounded concurrent diagnostic ring with monotonic sequence numbers, structured event serialization, redaction, and selected callback/dispatch WinEvent records.
- [x] 3.2 Instrument existing group/split/container/guest lifecycle and Shepherd repair paths with reason, before/attempt/result/after context without adding native behavior.
- [x] 3.3 Add read-only `--doctor` output and optional output path; include identity, Windows/runtime, monitors, GPU, persistence, process, native, logical-when-available, trace, and bounded log sections.
- [x] 3.4 Add explicit support-bundle export (ZIP or directory) and a header-independent diagnostic trigger; guarantee export has no SaveState, capture/release, activation, layout, or upload side effects.

## 4. Tests and documentation

- [x] 4.1 Add deterministic coverage for identity, CLI dispatch, state classification, title redaction, native observation helpers, trace sequencing/bounds/concurrency, and export non-mutation.
- [x] 4.2 Update README, architecture, testing guidance, and internal waypoint/state with the friend-machine workflow, privacy contract, performance bounds, and deferred Shepherd V2 work.

## 5. Gates and handoff

- [x] 5.1 Run project/solution/ValidationDriver/GuineaPig/Spike builds with zero warnings/errors, validation script, geometry self-test, OpenSpec validation, and diff check.
- [x] 5.2 Publish Release self-contained artifact twice as practical; verify `--version` commit, SHA-256, configuration/RID, and reproducibility notes.
- [x] 5.3 Perform privacy/security/architecture/performance review, inspect exact doctor/bundle contents, stage only intended changes, and create one unpushed coherent commit.
