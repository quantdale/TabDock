## Why

Regressions like H2 (drag-reorder oscillation), H4 (host-background smear), and H6 (container minimize releasing the active tab) are only discoverable through manual testing or live incidents today, even though the ValidationDriver harness already has the infrastructure to catch them automatically. Worse, several browser scenarios assert on instrumentation that no longer exists (`LAYOUT[*]`, `unhealthy` log lines — deleted with the Reparent backend), so they pass vacuously or can only fail, giving false confidence. The harness needs a surgical coverage pass: close the real gaps, retarget the stale assertions, and codify the input-injection safety discipline as enforceable requirements.

## What Changes

- **H6 regression scenario** — new `container-minimize-retains-tabs` scenario: capture ≥2 pigs, minimize the container, restore it, assert tab count unchanged, active guest re-docked, and zero misclassified tray-close release lines. Added to the `all` core set (pig-only, hermetic).
- **H2 oscillation upper bound** — extend `dragreorder` (and `browser-dragreorder`) to assert an upper bound on `Reordered tab` log lines per drag and zero immediate flip-back pairs; bug-present code currently passes the `>= 1` check.
- **Retarget stale browser assertions** — `browser-lifecycle`, `browser-tabswitch-hidesafety`, `browser-soak`, and `browser-multi` are re-aimed at Shepherd-era signals (`SHEPHERD[*]` log lines, rect/geometry checks, `PrintWindow` pixel checks) or have dead assertions removed. No assertion may reference instrumentation absent from committed source (the `docs/TESTING.md` §D rule, made enforceable).
- **Chromium render across tab switches** — extend `browser-tabswitch-hidesafety` (or add a dedicated scenario) to `PrintWindow`-verify the Chromium guest is live-rendering (brightness/variance thresholds) after each of N tab switches — hard assertions, not best-effort.
- **Second-tier user flows** (each a small pig-based scenario): held `Ctrl+Alt+G` opens exactly one capture picker; popping out an *inactive* tab does not change the active tab; an already-captured window is absent from / rejected by the picker (double-capture guard); persisted active-tab index survives the first restore+save cycle (extends the `persist-kill` pattern).
- **Safety discipline as spec** — the four input-injection rules from `docs/TESTING.md` (identity verification immediately before input, scope to owned windows, unconditional try/finally cleanup, single-run-then-report with re-verification) become explicit requirements every new or modified scenario must satisfy.

Explicitly out of scope: new ValidationDriver infrastructure (all scenarios build on existing primitives); changes to production TabDock code; fixing the `ForceForeground` environmental flake; the unresolved "H7/H9" numbering (no such entries exist in the repo — excluded until identified).

## Capabilities

### New Capabilities
- `e2e-scenario-coverage`: Which end-to-end user workflows the ValidationDriver must exercise automatically, what each scenario asserts, and the rule that assertions only reference instrumentation present in committed source.
- `e2e-input-safety`: The non-negotiable safety discipline for any automation that drives real windows or injects input — window-identity verification, ownership scoping, unconditional cleanup, and no blind retries.

### Modified Capabilities
(none — existing specs cover production capabilities; this change touches test tooling only.)

## Impact

- **Code**: `tests/ValidationDriver/TabDock.ValidationDriver/Scenarios.cs` (scenario edits/additions), possibly `Program.cs` (scenario registration/`AllOrder`), and `TabDock.GuineaPig` only if a scenario needs a new guest switch (goal: none).
- **Docs**: `docs/TESTING.md` scenario list and the §D stale-assertion note; `AGENTS.md` testing section if the scenario inventory changes.
- **No production behavior change.** No new dependencies, no new projects.
- **Relationship to `fix-test-tooling-and-logging`**: complementary — that change hardens tooling internals and explicitly excludes new coverage; this change adds the coverage on top of the same safety discipline.
