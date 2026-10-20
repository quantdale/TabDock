## 1. Establish current motion and real-control regressions

- [ ] 1.1 Preserve/integrate the existing uncommitted MotionProfile/template baseline and inventory all descriptor, control-event, Loaded and queued-callback attachments; do not replace user work with HEAD.
- [ ] 1.2 Add a reusable no-input STA dispatcher/clock test fixture and no-inline weak-reference allocation helpers without a new test-framework dependency.
- [ ] 1.3 Reproduce the 30-control enabled/reduced-motion retention failure with actual shared button templates and borders alongside non-feedback controls.

## 2. Introduce attachment ownership

- [ ] 2.1 Replace public Tag wiring flags with private per-element owner state retaining exact observer/handler identities and current attachment/template generation.
- [ ] 2.2 Make enable/load attach idempotent and unload/disable/template-change detach remove observers, owned clocks, queued callbacks and stale references.
- [ ] 2.3 Apply that lifecycle contract to button, row, checkmark, chevron, border-state and entrance behaviors; permit safe reload without global strong registries.

## 3. Prove cleanup and reusable feedback

- [ ] 3.1 Add repeated load/unload/re-enable/template-replacement tests asserting one live response, no inactive descriptor observers and rejection of obsolete callbacks.
- [ ] 3.2 Run `dotnet build TabDock.sln` plus targeted managed lifecycle tests in normal and reduced motion; require collection after teardown instead of accepting flat synthetic counters as proof.
- [ ] 3.3 Preserve existing resource-evidence synthetic labeling and add the actual-control result to appropriate headless Windows coverage without visible windows, production storage or input.
- [ ] 3.4 Record any A09 timing behavior incidentally corrected by deferred attachment so that its later change can add/verify missing regression coverage without gratuitous code edits; defer full suite until A02/A10 are safe.
