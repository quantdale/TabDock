## 1. Verify the current realized-template defect

- [ ] 1.1 Confirm A03's attachment lifecycle and the retained motion baseline; reproduce the first-load zero-opacity symptom against actual TdButton/derived templates, noting if the prerequisite already incidentally fixed it.
- [ ] 1.2 Add managed, no-input tests for realized hover/leave, pointer and keyboard pressed/release states, optional missing parts and initial already-active state.

## 2. Bind only current realized parts

- [ ] 2.1 Resolve named parts after template realization rather than treating TemplatedParent presence as readiness; avoid permanently marking a null-part lookup complete.
- [ ] 2.2 Reuse A03's owner for bounded generation-checked deferred resolution, current-part initialization and cleanup on unload/disable/template replacement; do not add polling or duplicate observers.
- [ ] 2.3 If A03 already satisfies the part-timing contract, retain its implementation and add only missing proof/coverage rather than gratuitous replacement code.

## 3. Verify lifecycle and interaction invariants

- [ ] 3.1 Test template replacement/reload with queued callbacks, exactly one current feedback response, benign missing parts and collectable obsolete parts.
- [ ] 3.2 Run `dotnet build TabDock.sln`, targeted button-template tests and A03 weak-reference tests under both motion policies; preserve commands, keyboard focus, palette and timing tokens.
- [ ] 3.3 Defer complete unit/Release gates until A02/A10 are safe; any visual/manual button smoke remains explicitly supervised rather than sending unattended input.
