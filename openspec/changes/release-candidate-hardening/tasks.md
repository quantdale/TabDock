## 1. Baseline and identity

- [x] 1.1 Record the clean PR-branch baseline, test count, binding/source-contract inventory, and release-gate commands in the campaign plan/state.
- [x] 1.2 Extend the picker target/window row with process-start identity and explicit Windows executable-path comparison without changing the native identity authority.
- [x] 1.3 Make production enumeration and final picker handoff reject unavailable or changed process/class identity; preserve title mutability and authoritative Shepherd validation.
- [x] 1.4 Add deterministic refresh continuity tests for same identity, PID/process-instance/class/path changes, disappearance, filtered selections, and stale submission.

## 2. Picker scale and asynchronous work

- [x] 2.1 Add a nesting-safe selection notification batch for Select All Visible and Clear, with one aggregate command-state update per bulk action.
- [x] 2.2 Coalesce background icon result dispatches while preserving cancellation, refresh-generation, row-ownership, and completion semantics.
- [x] 2.3 Add deterministic large-candidate tests/instrumentation boundaries for 100/500/1000 rows without wall-clock correctness gates.
- [x] 2.4 Re-run repeated picker/icon generation stability tests and fix any test synchronization defect exposed by the larger corpus.

## 3. Accessibility and keyboard behavior

- [x] 3.1 Restore focusable tab-strip traversal and visible focus states while preserving DisplayTabs/ActiveTab bindings and AutomationIds.
- [x] 3.2 Add keyboard activation paths for tab close/pop-out, split halves, split close, capture actions, rename, menus, and Escape/default/cancel behavior.
- [x] 3.3 Give picker rows, selection controls, tabs, split LEFT/RIGHT halves, recovery/admission states, and caption actions meaningful accessible names/help text.
- [x] 3.4 Add deterministic XAML/source-contract tests for focusability, keyboard handlers, critical AutomationIds, duplicate IDs, ContentHost, and binding direction.

## 4. Responsive visual system

- [x] 4.1 Consolidate shared focus, disabled, warning, control, and surface resources, including a dark coherent ComboBox and maintainable tooltip/list states.
- [x] 4.2 Audit and adjust launcher, standalone picker, inline picker, and container layout for small work areas, long text, and 100–200% scaling without changing native DPI placement.
- [x] 4.3 Improve discoverability copy/affordances for create, capture, workspace switching, rename/accent, tabs, pop-out, split/resume, and recovery attention.
- [x] 4.4 Add layout/source-contract tests for adaptive critical-control reachability and preserve the native ContentHost marker.

## 5. Projection and regression coverage

- [x] 5.1 Add launcher projection tests for zero/restored/created groups, live rename/accent/member-count changes, selection removal, activation, and capture admission transitions.
- [x] 5.2 Add container/tab/split regression contracts and deterministic coverage for ordinary tabs, composite halves, reorder/pop-out/close, dormant/presented split, workspace switching, and window chrome.
- [x] 5.3 Audit and update ValidationDriver scenario registration and helper contracts for the redesigned launcher/picker/keyboard/layout flows.
- [x] 5.4 Add explicit `BLOCKED_SUPERVISED`/`BLOCKED_ENVIRONMENT` entries and exact rerun commands where real desktop qualification cannot be executed safely.

## 6. Failure quality and release documentation

- [x] 6.1 Audit visible capture, admission, elevation, target-disappearance, split, rename, storage, recovery, and release failures for precise actionable wording.
- [x] 6.2 Update README, architecture/testing guidance, ship-readiness audit, OpenSpec task evidence, and known-issue/current-blocker language to match the final UI.
- [ ] 6.3 Verify release build/publish, version/provenance, vulnerability/release-tooling, native ABI, support-bundle privacy, Stage A/B boundaries, and candidate evidence requirements.

## 7. Final qualification and handoff

- [ ] 7.1 Run Debug and Release builds/tests, release-tooling, canonical validate/publish, OpenSpec validation, source-contract/accessibility tests, and diff check.
- [ ] 7.2 Repeat stability checks for picker icon/selection and any touched dispatch/concurrency path; root-fix flakes rather than widening timeouts.
- [ ] 7.3 Run feasible supervised ValidationDriver scenarios only on a safe exclusive desktop; otherwise record exact blocked scenarios and commands.
- [ ] 7.4 Commit coherent waves, push the existing PR branch, verify final SHA/remote/worktree/PR state, and update STATE with the complete evidence ledger.
