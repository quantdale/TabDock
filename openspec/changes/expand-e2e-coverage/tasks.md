# Tasks — expand-e2e-coverage

All work is in `tests/ValidationDriver/TabDock.ValidationDriver/` (primarily `Scenarios.cs`, `Program.cs`) plus docs. No production code changes. Every new/modified scenario must satisfy the `e2e-input-safety` spec: build only on existing primitives (`SpawnPig`/`SpawnNotepad`, `Discover` PID/class/title verification, `EnsureClickable`, `GuardedProc`, scenario try/finally `Cleanup`).

## 1. Retarget stale assertions (do first — stops false confidence)

- [ ] 1.1 In `browser-lifecycle` (`Scenarios.cs:2467`): remove the positive `LAYOUT[capture]` assertion; replace capture-success verification with `IsDocked` (guest rect == content-area marker) plus a fresh `SHEPHERD[*]` line for the guest — first grep the main app source to confirm the exact `SHEPHERD` substring asserted exists in committed code
- [ ] 1.2 Remove the vacuous `FindDriftWithoutPrecedingMovesize` checks from `browser-lifecycle` and `browser-tabswitch-hidesafety` (the `LAYOUT[drift]`/`LAYOUT[movesize]` instrumentation does not exist; behavioral successor is already covered by `chrometabdrag`/`dragout-by-titlebar`)
- [ ] 1.3 Remove the vacuous zero-`unhealthy` assertions from `browser-soak` and `browser-multi` (`RenderHealthService` was deleted); keep their EXCEPTION/aliveness checks
- [ ] 1.4 Grep the whole driver for any remaining log-substring assertions and verify each against main app source; fix or remove any others found

## 2. H2 — drag-reorder oscillation bound

- [ ] 2.1 Add a log-analysis helper that, for a drag's line range, counts `Reordered tab` lines and detects immediate flip-back pairs (X→Y directly followed by Y→X for the same tab)
- [ ] 2.2 In `dragreorder`: capture the log offset before the drag, then assert zero flip-back pairs and count ≤ bound (run once to observe the passing count, set the bound with generous headroom, record the observed value in a comment)
- [ ] 2.3 Apply the same assertions to `browser-dragreorder`
- [ ] 2.4 Sanity-check the detection: temporarily reason through (or hand-simulate) a flip-pair log sequence and confirm the helper flags it

## 3. H6 — container minimize retains tabs

- [ ] 3.1 Add `container-minimize-retains-tabs` scenario: spawn 2 pigs, `CaptureIntoGroup`, click the container minimize button (`ClickMinimizeButton`), restore, assert `TabCount` unchanged, zero `hid itself`/release lines attributable to the minimize, and active guest `IsDocked` after restore
- [ ] 3.2 Register the scenario in the dispatch table and `AllOrder`; verify it runs as part of `all`

## 4. H4 — Chromium render across tab switches

- [ ] 4.1 In `browser-tabswitch-hidesafety`: navigate the browser to the deterministic local test page (the `chromeinput` pattern), and after each switch to the browser tab capture it via `CaptureWindowViaPrintWindow` and assert brightness/variance above the floors proven in `maximize-repro`/`realapp-multi-render`
- [ ] 4.2 Keep thresholds tolerant (relative liveness, not exact pixels); record chosen constants with justification in comments

## 5. Second-tier pig flows (all join `AllOrder`)

- [ ] 5.1 `hotkey-hold-single-picker`: hold `Ctrl+Alt+G` ~2 s (key repeat), assert exactly one picker window, Esc-dismiss, assert zero
- [ ] 5.2 `popout-inactive-keeps-active`: 3 pigs in one group, make tab 3 active, pop out tab 1 via context menu, assert active tab is still the former tab 3 and its guest is docked/visible
- [ ] 5.3 `double-capture-refused`: capture a pig, reopen the picker (Ctrl+Alt+G), assert the captured pig's title is absent from the picker list (or selection is rejected) and the group is unchanged
- [ ] 5.4 `persist-active-tab-index`: extend the `persist-kill` pattern — persist a group with active tab index > 0, kill, relaunch, let the debounced save run, assert `state.json` still records the original index

## 6. Validation and docs

- [ ] 6.1 `dotnet build` the ValidationDriver project with zero warnings
- [ ] 6.2 Supervised run: `all` passes with the new scenarios included; individually run each modified/new browser scenario with an available `--guest`
- [ ] 6.3 Update `docs/TESTING.md`: scenario list, remove/rewrite the §A stale-instrumentation note for the retargeted scenarios, confirm §D reflects the now-enforced rule
- [ ] 6.4 Update `AGENTS.md` testing section if the scenario inventory or `all` composition changed
- [ ] 6.5 Final per-scenario audit against the four `e2e-input-safety` requirements (identity re-verification, ownership scoping, try/finally cleanup, no blind retries)
